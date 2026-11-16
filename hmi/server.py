# -*- coding: utf-8 -*-
"""装袋机 Web HMI 后端（直连 S7，不依赖 Kepware / OPC UA 权限）。

数据源：snap7 直连 PLC（S7-1200 / PLCSIM），读写 M 区位。
- M10.0 启动 / M10.1 急停 / M10.2 复位
- M30.0 ~ M31.7 共 16 个输出
- M32.0 心跳
连不上时自动 DEMO 模式。

启动：python server.py   打开：http://127.0.0.1:8080
"""
import json
import os
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

try:
    import snap7
    from snap7.type import Areas
    HAVE_S7 = True
except Exception:
    HAVE_S7 = False

BASE = os.path.dirname(os.path.abspath(__file__))
CONFIG_PATH = os.path.join(BASE, 'config.json')

DEFAULT_CONFIG = {
    "plc_ip": "192.168.250.200",
    "rack": 0,
    "slot": 1,
    "http_port": 8080,
    # 逻辑名 -> (M 字节偏移, 位号)
    "bits": {
        "启动按钮": [10, 0],
        "急停": [10, 1],
        "复位按钮": [10, 2],
        "上层使能": [30, 0],
        "主动轮使能": [30, 1],
        "链结使能": [30, 2],
        "切割器使能": [30, 3],
        "挡板使能": [30, 4],
        "挡板位置状态": [30, 5],
        "丝杆使能": [30, 6],
        "丝杆位置状态": [30, 7],
        "送料1使能": [31, 0],
        "送料1位置状态": [31, 1],
        "送料2使能": [31, 2],
        "送料2位置状态": [31, 3],
        "吸嘴使能": [31, 4],
        "吸嘴位置状态": [31, 5],
        "撑袋器使能": [31, 6],
        "撑袋器位置状态": [31, 7],
        "心跳": [32, 0]
    }
}


def load_config():
    cfg = dict(DEFAULT_CONFIG)
    if os.path.exists(CONFIG_PATH):
        try:
            with open(CONFIG_PATH, 'r', encoding='utf-8') as f:
                cfg.update(json.load(f))
        except Exception as e:
            print('[WARN] config.json 读取失败:', e)
    return cfg


CFG = load_config()
BITS = {k: tuple(v) for k, v in CFG['bits'].items()}
M_START = min(b for b, _ in BITS.values())      # 10
M_END = max(b for b, _ in BITS.values())        # 32
M_LEN = M_END - M_START + 1                     # 23

STATE = {'connected': False, 'demo': True, 'error': '', 'values': {}, 'last_update': 0}

_client = None
_lock = threading.Lock()


def s7_connect():
    client = snap7.client.Client()
    client.connect(CFG['plc_ip'], int(CFG.get('rack', 0)), int(CFG.get('slot', 1)))
    return client


def s7_read_all(client):
    data = client.read_area(Areas.MK, 0, M_START, M_LEN)
    values = {}
    for name, (byte, bit) in BITS.items():
        idx = byte - M_START
        values[name] = bool(data[idx] & (1 << bit))
    return values


def s7_write_bit(client, byte, bit, value):
    idx = byte - M_START
    cur = bytearray(client.read_area(Areas.MK, 0, M_START, M_LEN))
    if value:
        cur[idx] |= (1 << bit)
    else:
        cur[idx] &= ~(1 << bit) & 0xFF
    client.write_area(Areas.MK, 0, M_START, bytes(cur))


def s7_worker():
    global _client
    while True:
        try:
            client = s7_connect()
            with _lock:
                _client = client
            STATE['connected'] = True
            STATE['demo'] = False
            STATE['error'] = ''
            print('[S7] 已连接', CFG['plc_ip'])
            while True:
                values = s7_read_all(client)
                STATE['values'] = values
                STATE['last_update'] = time.time()
                time.sleep(0.4)
        except Exception as e:
            with _lock:
                _client = None
            STATE['connected'] = False
            STATE['error'] = '%s: %s' % (type(e).__name__, e)
            if not STATE['demo']:
                print('[S7] 断开:', STATE['error'])
            time.sleep(3)


def demo_loop():
    durations = [15, 2, 2, 2, 8, 3, 5, 3, 3, 8, 2]
    idx, t0 = 0, time.time()
    while True:
        if STATE['connected']:
            time.sleep(1)
            continue
        if time.time() - t0 > durations[idx]:
            t0, idx = time.time(), (idx + 1) % 11
        step = idx + 1
        STATE['values'] = {
            '启动按钮': False, '急停': False, '复位按钮': False,
            '上层使能': True, '主动轮使能': True, '链结使能': True, '切割器使能': True,
            '挡板使能': True, '丝杆使能': True, '送料1使能': True, '送料2使能': True,
            '吸嘴使能': True, '撑袋器使能': True,
            '挡板位置状态': 2 <= step <= 10,
            '丝杆位置状态': not (2 <= step <= 9),
            '送料1位置状态': step not in (6, 7),
            '送料2位置状态': step not in (6, 7),
            '吸嘴位置状态': step != 2,
            '撑袋器位置状态': step >= 4,
            '心跳': int(time.time() * 2) % 2 == 0,
            '_step': step,
        }
        STATE['last_update'] = time.time()
        time.sleep(0.3)


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        pass

    def _send(self, code, body, ctype='application/json; charset=utf-8'):
        data = body if isinstance(body, bytes) else body.encode('utf-8')
        self.send_response(code)
        self.send_header('Content-Type', ctype)
        self.send_header('Content-Length', str(len(data)))
        self.send_header('Cache-Control', 'no-store')
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        u = urlparse(self.path)
        if u.path in ('/', '/index.html'):
            with open(os.path.join(BASE, 'index.html'), 'rb') as f:
                self._send(200, f.read(), 'text/html; charset=utf-8')
            return
        if u.path == '/api/state':
            self._send(200, json.dumps({
                'connected': STATE['connected'],
                'demo': STATE['demo'],
                'error': STATE['error'],
                'tags_found': len(BITS),
                'values': STATE['values'],
                'age': round(time.time() - STATE['last_update'], 1) if STATE['last_update'] else None,
            }, ensure_ascii=False))
            return
        self._send(404, '{"error":"not found"}')

    def do_POST(self):
        u = urlparse(self.path)
        q = parse_qs(u.query)
        if u.path != '/api/write':
            self._send(404, '{"error":"not found"}')
            return
        name = (q.get('name') or [''])[0]
        val = (q.get('value') or ['0'])[0].lower() in ('1', 'true', 'on')
        if name not in BITS:
            self._send(200, json.dumps({'ok': False, 'reason': 'unknown tag: ' + name}, ensure_ascii=False))
            return
        if not STATE['connected'] or _client is None:
            self._send(200, json.dumps({'ok': False, 'reason': 'not connected'}, ensure_ascii=False))
            return
        try:
            byte, bit = BITS[name]
            with _lock:
                s7_write_bit(_client, byte, bit, val)
            self._send(200, json.dumps({'ok': True}, ensure_ascii=False))
        except Exception as e:
            self._send(200, json.dumps({'ok': False, 'reason': '%s: %s' % (type(e).__name__, e)}, ensure_ascii=False))


def main():
    if not HAVE_S7:
        print('[WARN] 没装 python-snap7，只能 DEMO 模式')
    threading.Thread(target=s7_worker, daemon=True).start()
    threading.Thread(target=demo_loop, daemon=True).start()
    port = int(CFG.get('http_port', 8080))
    srv = ThreadingHTTPServer(('127.0.0.1', port), Handler)
    print('[HMI] http://127.0.0.1:%d' % port)
    print('[HMI] PLC:', CFG['plc_ip'])
    srv.serve_forever()


if __name__ == '__main__':
    main()

# -*- coding: utf-8 -*-
"""装袋机 Web HMI 后端。

数据源：Kepware KEPServerEX 的 OPC UA 服务器（默认 opc.tcp://127.0.0.1:49320）
- 只读/写布尔量（按钮位、输出位）
- 如果 OPC UA 连不上，自动进入 DEMO 模式（用内存里的假数据，方便先看界面）

启动：python server.py
打开：http://127.0.0.1:8080
"""
import asyncio
import json
import os
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

try:
    from asyncua import Client
    HAVE_UA = True
except Exception:
    HAVE_UA = False

BASE = os.path.dirname(os.path.abspath(__file__))
CONFIG_PATH = os.path.join(BASE, 'config.json')

DEFAULT_CONFIG = {
    "ua_url": "opc.tcp://127.0.0.1:49320",
    "ua_user": "",
    "ua_password": "",
    "http_port": 8080,
    "tag_map": {
        "启动按钮": "启动按钮",
        "急停": "急停",
        "复位按钮": "复位按钮",
        "上层使能": "上层使能",
        "主动轮使能": "主动轮使能",
        "链结使能": "链结使能",
        "切割器使能": "切割器使能",
        "挡板使能": "挡板使能",
        "挡板位置状态": "挡板位置状态",
        "丝杆使能": "丝杆使能",
        "丝杆位置状态": "丝杆位置状态",
        "送料1使能": "送料1使能",
        "送料1位置状态": "送料1位置状态",
        "送料2使能": "送料2使能",
        "送料2位置状态": "送料2位置状态",
        "吸嘴使能": "吸嘴使能",
        "吸嘴位置状态": "吸嘴位置状态",
        "撑袋器使能": "撑袋器使能",
        "撑袋器位置状态": "撑袋器位置状态",
        "心跳": "心跳"
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
STATE = {
    'connected': False,
    'demo': True,
    'error': '',
    'values': {},
    'last_update': 0,
}
NODES = {}          # 逻辑名 -> nodeid
UA_LOCK = threading.Lock()


# ---------------- OPC UA 后台线程 ----------------

def ua_worker():
    url = CFG['ua_url']
    while True:
        try:
            asyncio.run(ua_session(url))
        except Exception as e:
            STATE['connected'] = False
            STATE['error'] = '%s: %s' % (type(e).__name__, e)
            print('[UA] 断开:', STATE['error'])
        time.sleep(3)


async def ua_session(url):
    async with Client(url=url, timeout=8) as client:
        if CFG.get('ua_user'):
            await client.set_user(CFG['ua_user'])
            await client.set_password(CFG.get('ua_password', ''))
        STATE['connected'] = True
        STATE['demo'] = False
        STATE['error'] = ''
        print('[UA] 已连接', url)

        # 建立 逻辑名 -> nodeid 映射：按显示名精确匹配
        mapping = CFG['tag_map']
        found = {}
        root = client.nodes.objects
        stack = [(root, 0)]
        while stack:
            node, depth = stack.pop()
            if depth > 6:
                continue
            try:
                children = await node.get_children()
            except Exception:
                continue
            for ch in children:
                try:
                    dn = (await ch.read_display_name()).Text
                except Exception:
                    continue
                for logical, tag_name in mapping.items():
                    if logical not in found and dn == tag_name:
                        found[logical] = ch.nodeid
                stack.append((ch, depth + 1))

        with UA_LOCK:
            NODES.clear()
            NODES.update(found)
        print('[UA] 匹配到 tag:', len(found), '/', len(mapping))

        while True:
            values = {}
            for logical, nodeid in list(NODES.items()):
                try:
                    node = client.get_node(nodeid)
                    v = await node.read_value()
                    values[logical] = bool(v) if isinstance(v, (bool, int, float)) else v
                except Exception:
                    values[logical] = None
            STATE['values'] = values
            STATE['last_update'] = time.time()
            await asyncio.sleep(0.5)


def demo_loop():
    """没有 OPC UA 时的演示数据：模拟 11 步循环。"""
    steps = 11
    durations = [15, 2, 2, 2, 8, 3, 5, 3, 3, 8, 2]
    idx = 0
    t0 = time.time()
    while True:
        if STATE['connected']:
            time.sleep(1)
            continue
        elapsed = time.time() - t0
        if elapsed > durations[idx]:
            t0 = time.time()
            idx = (idx + 1) % steps
        step = idx + 1
        v = {
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
        STATE['values'] = v
        STATE['last_update'] = time.time()
        time.sleep(0.3)


# ---------------- HTTP ----------------

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
            p = os.path.join(BASE, 'index.html')
            with open(p, 'rb') as f:
                self._send(200, f.read(), 'text/html; charset=utf-8')
            return
        if u.path == '/api/state':
            self._send(200, json.dumps({
                'connected': STATE['connected'],
                'demo': STATE['demo'],
                'error': STATE['error'],
                'tags_found': len(NODES),
                'values': STATE['values'],
                'age': round(time.time() - STATE['last_update'], 1) if STATE['last_update'] else None,
            }, ensure_ascii=False))
            return
        self._send(404, '{"error":"not found"}')

    def do_POST(self):
        u = urlparse(self.path)
        q = parse_qs(u.query)
        if u.path == '/api/write':
            name = (q.get('name') or [''])[0]
            val = (q.get('value') or ['0'])[0].lower() in ('1', 'true', 'on')
            nodeid = NODES.get(name)
            if nodeid is None:
                self._send(200, json.dumps({'ok': False, 'reason': 'tag not mapped: ' + name}, ensure_ascii=False))
                return
            if not STATE['connected']:
                self._send(200, json.dumps({'ok': False, 'reason': 'not connected (demo mode)'}, ensure_ascii=False))
                return
            try:
                # 直接写：用独立事件循环
                asyncio.run(write_tag(nodeid, val))
                self._send(200, json.dumps({'ok': True}, ensure_ascii=False))
            except Exception as e:
                self._send(200, json.dumps({'ok': False, 'reason': '%s: %s' % (type(e).__name__, e)}, ensure_ascii=False))
            return
        self._send(404, '{"error":"not found"}')


async def write_tag(nodeid, value):
    async with Client(url=CFG['ua_url'], timeout=8) as client:
        if CFG.get('ua_user'):
            await client.set_user(CFG['ua_user'])
            await client.set_password(CFG.get('ua_password', ''))
        node = client.get_node(nodeid)
        await node.write_value(bool(value))


def main():
    threading.Thread(target=ua_worker, daemon=True).start()
    threading.Thread(target=demo_loop, daemon=True).start()
    port = int(CFG.get('http_port', 8080))
    srv = ThreadingHTTPServer(('127.0.0.1', port), Handler)
    print('[HMI] http://127.0.0.1:%d' % port)
    print('[HMI] OPC UA:', CFG['ua_url'], '(连不上则自动 DEMO 模式)')
    srv.serve_forever()


if __name__ == '__main__':
    main()

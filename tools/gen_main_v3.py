# -*- coding: utf-8 -*-
"""按 02_装袋机时序.scl（v3）的逻辑，生成 LAD 版 FB_GEN_MAIN。

v3 语义：
- 3 个按钮：启动 / 急停 / 复位
- Run 锁存；EnAll = (Run OR Reset) AND NOT EStop；10 个使能 = EnAll
- 11 步纯时间推进；6 个位置状态由步号决定（极性按说明 bake 进逻辑）
- 无 OR 分支：用"双线圈"合法特性拆分条件（已实测 TIA 允许）
"""
import io

OUT = r"D:\harness工作区\tia-lad-gen\gen_main.xml"
NS_FLG = "http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v5"
NS_IF = "http://www.siemens.com/automation/Openness/SW/Interface/v5"
CULTURE = "zh-CN"
G = "GlobalVariable"
L = "LocalVariable"
PREFIX = "V3M_"
PREFIXED = ("启动按钮", "急停", "复位按钮")

TIMES = {
    "T_Feed": "T#15s", "T_NozzleDown": "T#2s", "T_NozzleUp": "T#2s",
    "T_BagOpen": "T#2s", "T_ScrewDown": "T#8s", "T_FeedFwd": "T#3s",
    "T_Load": "T#5s", "T_FeedBack": "T#3s", "T_Wait": "T#3s",
    "T_ScrewUp": "T#8s", "T_Baffle": "T#2s",
}

ENABLES = ["上层使能", "主动轮使能", "链结使能", "切割器使能", "挡板使能",
           "丝杆使能", "送料1使能", "送料2使能", "吸嘴使能", "撑袋器使能"]

POS = ["挡板位置状态", "丝杆位置状态", "送料1位置状态", "送料2位置状态",
       "吸嘴位置状态", "撑袋器位置状态"]


class Net:
    def __init__(self, title):
        self.title = title
        self.parts = []
        self.wires = []
        self.uid = 20

    def nid(self):
        self.uid += 1
        return self.uid

    def _access(self, name, scope, negated=False):
        a = self.nid()
        self.parts.append('<Access Scope="%s" UId="%d"><Symbol><Component Name="%s" /></Symbol></Access>'
                          % (scope, a, name))
        return a

    def contact(self, name, scope=G, negated=False):
        if scope == G and name in PREFIXED:
            name = PREFIX + name
        a = self._access(name, scope)
        p = self.nid()
        neg = '<Negated Name="operand" />' if negated else ''
        self.parts.append('<Part Name="Contact" UId="%d">%s</Part>' % (p, neg))
        w = self.nid()
        self.wires.append('<Wire UId="%d"><NameCon UId="%d" Name="operand" /><IdentCon UId="%d" /></Wire>'
                          % (w, p, a))
        return p

    def coil(self, name, kind="Coil", scope=G, negated=False):
        if scope == G and name in PREFIXED:
            name = PREFIX + name
        a = self._access(name, scope)
        p = self.nid()
        neg = '<Negated Name="operand" />' if negated else ''
        self.parts.append('<Part Name="%s" UId="%d">%s</Part>' % (kind, p, neg))
        w = self.nid()
        self.wires.append('<Wire UId="%d"><NameCon UId="%d" Name="operand" /><IdentCon UId="%d" /></Wire>'
                          % (w, p, a))
        return p

    def ton(self, instance, pt_member):
        inst = self.nid()
        p = self.nid()
        self.parts.append(
            '<Part Name="TON" UId="%d" Version="1.0">'
            '<Instance Scope="%s" UId="%d"><Component Name="%s" /></Instance>'
            '<TemplateValue Name="time_type" Type="Type">Time</TemplateValue>'
            '</Part>' % (p, L, inst, instance))
        a = self._access(pt_member, L)
        w = self.nid()
        self.wires.append('<Wire UId="%d"><NameCon UId="%d" Name="PT" /><IdentCon UId="%d" /></Wire>' % (w, p, a))
        return p

    def chain(self, uids):
        w = self.nid()
        self.wires.append('<Wire UId="%d"><Powerrail /><NameCon UId="%d" Name="in" /></Wire>' % (w, uids[0]))
        for i in range(len(uids) - 1):
            w = self.nid()
            self.wires.append('<Wire UId="%d"><NameCon UId="%d" Name="out" /><NameCon UId="%d" Name="in" /></Wire>'
                              % (w, uids[i], uids[i + 1]))
        w = self.nid()
        oc = self.nid()
        self.wires.append('<Wire UId="%d"><NameCon UId="%d" Name="out" /><OpenCon UId="%d" /></Wire>'
                          % (w, uids[-1], oc))

    def timer_net(self, in_part, timer, pt, done):
        t = self.ton(timer, pt)
        d = self.coil(done, scope=L)
        w = self.nid()
        self.wires.append('<Wire UId="%d"><NameCon UId="%d" Name="out" /><NameCon UId="%d" Name="IN" /></Wire>'
                          % (w, in_part, t))
        w = self.nid()
        self.wires.append('<Wire UId="%d"><NameCon UId="%d" Name="Q" /><NameCon UId="%d" Name="in" /></Wire>'
                          % (w, t, d))
        w = self.nid()
        oc = self.nid()
        self.wires.append('<Wire UId="%d"><NameCon UId="%d" Name="ET" /><OpenCon UId="%d" /></Wire>' % (w, t, oc))
        w = self.nid()
        oc2 = self.nid()
        self.wires.append('<Wire UId="%d"><NameCon UId="%d" Name="out" /><OpenCon UId="%d" /></Wire>' % (w, d, oc2))
        return t

    def power_target(self, first_uid):
        w = self.nid()
        self.wires.append('<Wire UId="%d"><Powerrail /><NameCon UId="%d" Name="in" /></Wire>' % (w, first_uid))

    def to_xml(self, cu_id):
        return (
            '      <SW.Blocks.CompileUnit ID="%d" CompositionName="CompileUnits">\n'
            '        <AttributeList>\n'
            '          <NetworkSource>\n'
            '            <FlgNet xmlns="%s">\n'
            '              <Parts>\n%s\n              </Parts>\n'
            '              <Wires>\n%s\n              </Wires>\n'
            '            </FlgNet>\n'
            '          </NetworkSource>\n'
            '          <ProgrammingLanguage>LAD</ProgrammingLanguage>\n'
            '        </AttributeList>\n'
            '        <ObjectList>\n'
            '          <MultilingualText ID="%d" CompositionName="Title">\n'
            '            <ObjectList><MultilingualTextItem ID="%d" CompositionName="Items">\n'
            '              <AttributeList><Culture>%s</Culture><Text>%s</Text></AttributeList>\n'
            '            </MultilingualTextItem></ObjectList>\n'
            '          </MultilingualText>\n'
            '          <MultilingualText ID="%d" CompositionName="Comment">\n'
            '            <ObjectList><MultilingualTextItem ID="%d" CompositionName="Items">\n'
            '              <AttributeList><Culture>%s</Culture><Text></Text></AttributeList>\n'
            '            </MultilingualTextItem></ObjectList>\n'
            '          </MultilingualText>\n'
            '        </ObjectList>\n'
            '      </SW.Blocks.CompileUnit>\n'
        ) % (cu_id, NS_FLG,
             "\n".join("                " + p for p in self.parts),
             "\n".join("                " + w for w in self.wires),
             1000 + cu_id * 4, 1001 + cu_id * 4, CULTURE, self.title,
             1002 + cu_id * 4, 1003 + cu_id * 4, CULTURE)


nets = []

# 1. 启动按钮 -> S Run
n = Net("启动按钮 -> 置位 Run")
c = n.contact("启动按钮")
n.chain([c, n.coil("Run", kind="SCoil", scope=L)])
nets.append(n)

# 2. 急停 -> R Run
n = Net("急停 -> 复位 Run")
c = n.contact("急停")
n.chain([c, n.coil("Run", kind="RCoil", scope=L)])
nets.append(n)

# 3. 复位按钮 -> R Run
n = Net("复位按钮 -> 复位 Run")
c = n.contact("复位按钮")
n.chain([c, n.coil("Run", kind="RCoil", scope=L)])
nets.append(n)

# 4/5/6. EnAll = (Run OR 复位) AND NOT 急停 —— 用 S/R 线圈实现，避免双线圈覆盖
n = Net("复位 -> 置位 EnAll")
c = n.contact("复位按钮")
n.chain([c, n.coil("EnAll", kind="SCoil", scope=L)])
nets.append(n)

n = Net("Run -> 置位 EnAll")
c = n.contact("Run", scope=L)
n.chain([c, n.coil("EnAll", kind="SCoil", scope=L)])
nets.append(n)

n = Net("非 Run 且 非复位 -> 复位 EnAll")
a = n.contact("Run", scope=L, negated=True)
b = n.contact("复位按钮", negated=True)
n.chain([a, b, n.coil("EnAll", kind="RCoil", scope=L)])
nets.append(n)

n = Net("急停 -> 复位 EnAll")
c = n.contact("急停")
n.chain([c, n.coil("EnAll", kind="RCoil", scope=L)])
nets.append(n)

# 7. 10 个使能 = EnAll
n = Net("10 个使能 = EnAll")
c = n.contact("EnAll", scope=L)
chain = [c]
for name in ENABLES:
    chain.append(n.coil(name))
n.chain(chain)
nets.append(n)

# 8. 步转移 1..10
for i in range(1, 11):
    n = Net("步%d -> 步%d" % (i, i + 1))
    a = n.contact("Step_%d" % i, scope=L)
    b = n.contact("Done_%d" % i, scope=L)
    n.chain([a, b,
             n.coil("Step_%d" % (i + 1), kind="SCoil", scope=L),
             n.coil("Step_%d" % i, kind="RCoil", scope=L)])
    nets.append(n)

# 9. 步11 -> 步1
n = Net("步11 -> 步1（循环）")
a = n.contact("Step_11", scope=L)
b = n.contact("Done_11", scope=L)
n.chain([a, b, n.coil("Step_1", kind="SCoil", scope=L), n.coil("Step_11", kind="RCoil", scope=L)])
nets.append(n)

# 10. Idle = 无步激活
n = Net("Idle：所有步都没激活")
chain = []
for i in range(1, 12):
    chain.append(n.contact("Step_%d" % i, scope=L, negated=True))
chain.append(n.coil("Idle", scope=L))
n.chain(chain)
nets.append(n)

# 10b. 调试自动运行：上电自动置位 Run（DebugAutoRun 默认 true，不看任何按钮）
n = Net("调试自动运行 -> 置位 Run")
a = n.contact("DebugAutoRun", scope=L)
b = n.contact("Idle", scope=L)
n.chain([a, b, n.coil("Run", kind="SCoil", scope=L)])
nets.append(n)

# 11. Run + Idle -> 步1
n = Net("Run 且 Idle -> 置位步1")
a = n.contact("Run", scope=L)
b = n.contact("Idle", scope=L)
n.chain([a, b, n.coil("Step_1", kind="SCoil", scope=L)])
nets.append(n)

# 12/13. 急停/复位 -> 复位所有步
for label in ["急停", "复位按钮"]:
    n = Net("%s -> 复位所有步" % label)
    chain = [n.contact(label)]
    for i in range(1, 12):
        chain.append(n.coil("Step_%d" % i, kind="RCoil", scope=L))
    n.chain(chain)
    nets.append(n)

# 14. 定时器
timer_map = {
    1: ("T_Feed", None), 2: ("T_NozzleDown", None), 3: ("T_NozzleUp", None),
    4: ("T_BagOpen", None), 5: ("T_ScrewDown", "In5"), 6: ("T_FeedFwd", None),
    7: ("T_Load", None), 8: ("T_FeedBack", None), 9: ("T_Wait", None),
    10: ("T_ScrewUp", None), 11: ("T_Baffle", None),
}
for i in range(2, 6):
    n = Net("步%d 参与下降计时" % i)
    c = n.contact("Step_%d" % i, scope=L)
    n.chain([c, n.coil("In5", scope=L)])
    nets.append(n)

for i in range(1, 12):
    pt, special = timer_map[i]
    n = Net("步%d 定时器 %s" % (i, pt))
    if special:
        c = n.contact(special, scope=L)
    else:
        c = n.contact("Step_%d" % i, scope=L)
    n.power_target(c)
    n.timer_net(c, "Timer_%d" % i, pt, "Done_%d" % i)
    nets.append(n)

# 15. 位置状态输出：每步一组
def pos_values(step):
    """返回 {名称: True/False}"""
    v = {}
    if step == 0:
        v["挡板位置状态"] = False
        v["丝杆位置状态"] = True
        v["送料1位置状态"] = True
        v["送料2位置状态"] = True
        v["吸嘴位置状态"] = True
        v["撑袋器位置状态"] = False
    else:
        v["挡板位置状态"] = 2 <= step <= 10
        v["丝杆位置状态"] = not (2 <= step <= 9)
        v["送料1位置状态"] = step not in (6, 7)
        v["送料2位置状态"] = step not in (6, 7)
        v["吸嘴位置状态"] = step != 2
        v["撑袋器位置状态"] = step >= 4
    return v

for step in range(0, 12):
    n = Net("步%d 位置状态输出" % step if step else "待机位置状态输出")
    if step == 0:
        c = n.contact("Idle", scope=L)
    else:
        c = n.contact("Step_%d" % step, scope=L)
    chain = [c]
    vals = pos_values(step)
    for name in POS:
        if vals[name]:
            chain.append(n.coil(name, kind="SCoil"))
        else:
            chain.append(n.coil(name, kind="RCoil"))
    n.chain(chain)
    nets.append(n)

# 16. 心跳：1 秒翻转（调试用，证明 PLC 在跑）
n = Net("心跳：1 秒翻转")
a = n.contact("Heart", scope=L, negated=True)
n.power_target(a)
t = n.ton("Timer_Heart", "T_Heart")
d = n.coil("Heart", scope=L)
w = n.nid()
n.wires.append('<Wire UId="%d"><NameCon UId="%d" Name="out" /><NameCon UId="%d" Name="IN" /></Wire>' % (w, a, t))
w = n.nid()
n.wires.append('<Wire UId="%d"><NameCon UId="%d" Name="Q" /><NameCon UId="%d" Name="in" /></Wire>' % (w, t, d))
w = n.nid()
oc = n.nid()
n.wires.append('<Wire UId="%d"><NameCon UId="%d" Name="ET" /><OpenCon UId="%d" /></Wire>' % (w, t, oc))
w = n.nid()
oc2 = n.nid()
n.wires.append('<Wire UId="%d"><NameCon UId="%d" Name="out" /><OpenCon UId="%d" /></Wire>' % (w, d, oc2))
nets.append(n)

n = Net("心跳输出")
a = n.contact("Heart", scope=L)
n.chain([a, n.coil("心跳")])
nets.append(n)

# ---- 组装 FB ----
statics = []
statics.append('            <Member Name="DebugAutoRun" Datatype="Bool"><StartValue>false</StartValue></Member>')
for name in ["Run", "EnAll", "Idle", "In5"]:
    statics.append('            <Member Name="%s" Datatype="Bool" />' % name)
for i in range(1, 12):
    statics.append('            <Member Name="Step_%d" Datatype="Bool" />' % i)
for i in range(1, 12):
    statics.append('            <Member Name="Done_%d" Datatype="Bool" />' % i)
for i in range(1, 12):
    statics.append('            <Member Name="Timer_%d" Datatype="TON_TIME" Version="1.0" />' % i)
statics.append('            <Member Name="Timer_Heart" Datatype="TON_TIME" Version="1.0" />')
statics.append('            <Member Name="Heart" Datatype="Bool" />')
inputs = []
for name, val in TIMES.items():
    inputs.append('            <Member Name="%s" Datatype="Time"><StartValue>%s</StartValue></Member>' % (name, val))
inputs.append('            <Member Name="T_Heart" Datatype="Time"><StartValue>T#1s</StartValue></Member>')


body = "\n".join(n.to_xml(idx + 1) for idx, n in enumerate(nets))

xml = (
    '<?xml version="1.0" encoding="utf-8"?>\n'
    '<Document>\n'
    '  <Engineering version="V21" />\n'
    '  <SW.Blocks.FB ID="0">\n'
    '    <AttributeList>\n'
    '      <Interface>\n'
    '        <Sections xmlns="%s">\n'
    '          <Section Name="Input">\n%s\n          </Section>\n'
    '          <Section Name="Output" />\n'
    '          <Section Name="InOut" />\n'
    '          <Section Name="Static">\n%s\n          </Section>\n'
    '          <Section Name="Temp" />\n'
    '          <Section Name="Constant" />\n'
    '        </Sections>\n'
    '      </Interface>\n'
    '      <MemoryLayout>Optimized</MemoryLayout>\n'
    '      <Name>FB_GEN_MAIN</Name>\n'
    '      <Namespace></Namespace>\n'
    '      <Number>9100</Number>\n'
    '      <ProgrammingLanguage>LAD</ProgrammingLanguage>\n'
    '    </AttributeList>\n'
    '    <ObjectList>\n'
    '%s'
    '    </ObjectList>\n'
    '  </SW.Blocks.FB>\n'
    '</Document>\n'
) % (NS_IF, "\n".join(inputs), "\n".join(statics), body)

with io.open(OUT, "w", encoding="utf-8", newline="\n") as f:
    f.write(xml)
print("saved:", OUT, len(xml), "chars,", len(nets), "networks")









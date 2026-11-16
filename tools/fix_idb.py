# -*- coding: utf-8 -*-
"""把导出的实例 DB 加上 StartValue（属性名/初值），用于重新导入以设初值。"""
import io

SRC = r"D:\TiaLadGen\out\blocks\PLC_1_FB_GEN_MAIN_DB.xml"
DST = r"D:\TiaLadGen\out\gen_idb_fix.xml"

TIMES = {
    "T_Feed": "T#15s", "T_NozzleDown": "T#2s", "T_NozzleUp": "T#2s",
    "T_BagOpen": "T#1s", "T_ScrewDown": "T#8s", "T_FeedFwd": "T#3s",
    "T_Load": "T#5s", "T_FeedBack": "T#3s", "T_Wait": "T#3s",
    "T_ScrewUp": "T#8s", "T_Baffle": "T#1s", "T_Heart": "T#1s",
}

with io.open(SRC, "r", encoding="utf-8") as f:
    text = f.read()

count = 0
for name, val in TIMES.items():
    old = '<Member Name="%s" Datatype="Time" />' % name
    new = '<Member Name="%s" Datatype="Time"><StartValue>%s</StartValue></Member>' % (name, val)
    if old in text:
        text = text.replace(old, new)
        count += 1
    else:
        print("MISS", name)

old = '<Member Name="DebugAutoRun" Datatype="Bool" />'
new = '<Member Name="DebugAutoRun" Datatype="Bool"><StartValue>true</StartValue></Member>'
if old in text:
    text = text.replace(old, new)
    count += 1
else:
    print("MISS DebugAutoRun")

# 去掉 DocumentInfo（导入不需要）
start = text.find("<DocumentInfo>")
end = text.find("</DocumentInfo>")
if start >= 0 and end > start:
    text = text[:start] + text[end + len("</DocumentInfo>"):]

with io.open(DST, "w", encoding="utf-8", newline="\n") as f:
    f.write(text)

print("patched members:", count, "->", DST)

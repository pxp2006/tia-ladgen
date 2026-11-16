# -*- coding: utf-8 -*-
"""生成 LAD 生成器的点位表模板（Excel）。"""
from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side
from openpyxl.worksheet.datavalidation import DataValidation

OUT = r"D:\harness工作区\tia-lad-gen\点位表_模板.xlsx"

wb = Workbook()
ws = wb.active
ws.title = "点位表"

headers = ["工位", "模式", "输入变量", "输入地址", "输出变量", "输出地址", "延时ms", "备注"]
widths = [16, 12, 14, 12, 14, 12, 10, 30]
for i, (h, w) in enumerate(zip(headers, widths), start=1):
    c = ws.cell(row=1, column=i, value=h)
    c.font = Font(bold=True, color="FFFFFF")
    c.fill = PatternFill("solid", fgColor="1F4E78")
    c.alignment = Alignment(horizontal="center", vertical="center")
    ws.column_dimensions[c.column_letter].width = w
ws.row_dimensions[1].height = 22

sample = [
    ["工位1-启动", "Direct", "DI_1", "I0.0", "DO_1", "Q0.0", 0, "DI 直连 DO"],
    ["工位2-启动", "Direct", "DI_2", "I0.1", "DO_2", "Q0.1", 0, ""],
    ["工位3-启动", "Direct", "DI_3", "I0.2", "DO_3", "Q0.2", 0, ""],
    ["工位4-启动", "Direct", "DI_4", "I0.3", "DO_4", "Q0.3", 0, ""],
    ["工位5-启动", "Direct", "DI_5", "I0.4", "DO_5", "Q0.4", 0, ""],
    ["工位6-启动", "Direct", "DI_6", "I0.5", "DO_6", "Q0.5", 0, ""],
]
thin = Side(style="thin", color="BFBFBF")
border = Border(left=thin, right=thin, top=thin, bottom=thin)
for r, row in enumerate(sample, start=2):
    for col, val in enumerate(row, start=1):
        c = ws.cell(row=r, column=col, value=val)
        c.border = border
        c.alignment = Alignment(horizontal="center" if col in (2, 3, 4, 5, 6, 7) else "left")

dv = DataValidation(type="list", formula1='"Direct,StartStop"', allow_blank=False)
ws.add_data_validation(dv)
dv.add("B2:B200")

note = wb.create_sheet("填写说明")
lines = [
    ("LAD 点位表填写说明", True),
    ("", False),
    ("1. 一行 = 一个梯形图回路（一个网络）。", False),
    ("2. 模式：", True),
    ("   Direct    = 输入触点直接驱动输出线圈（I0.0 → Q0.0）。当前版本已支持。", False),
    ("   StartStop = 启保停（启动/停止 + 自锁）。下一版本支持。", False),
    ("3. 变量名：会自动写入 TIA 的变量表 LADGEN_Tags；已存在则跳过。", False),
    ("4. 地址格式：I0.0 / Q0.0 / M0.0 / DB1.DBX0.0（按 TIA 变量表规则）。", False),
    ("5. 延时ms：StartStop 模式下停止延时，当前版本填 0。", False),
    ("6. 表头行不要改；数据从第 2 行开始，空行会被跳过。", False),
    ("7. 生成流程：本表 → 导出 CSV（UTF-8）→ 生成器读取 → 建变量 → 生成 LAD → 导入 TIA → 编译。", False),
    ("8. 生成的块：FC_LADGEN（编号 9100），每次覆盖重建。", False),
]
for i, (text, bold) in enumerate(lines, start=1):
    c = note.cell(row=i, column=1, value=text)
    c.font = Font(bold=bold, size=14 if i == 1 else 11)
note.column_dimensions["A"].width = 110

wb.save(OUT)
print("saved:", OUT)

# -*- coding: utf-8 -*-
import openpyxl

wb = openpyxl.load_workbook(r"C:\Users\asus\Desktop\装袋机PLC程序\01_变量表.xlsx", data_only=True)
for ws in wb.worksheets:
    print("=== sheet:", ws.title, "rows:", ws.max_row, "cols:", ws.max_column)
    for row in ws.iter_rows(values_only=True):
        vals = [str(c) if c is not None else "" for c in row]
        if any(v.strip() for v in vals):
            print(" | ".join(vals))

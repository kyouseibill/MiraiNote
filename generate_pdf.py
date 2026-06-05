#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""将 Markdown 文件转换为 PDF - 使用 reportlab"""

import sys
from pathlib import Path
import re

from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import cm
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, PageBreak, Table, TableStyle
from reportlab.lib import colors

def parse_markdown(content):
    """解析 Markdown 内容为 reportlab 元素"""
    elements = []
    font_name = 'Helvetica'
    
    # 获取样式表
    styles = getSampleStyleSheet()
    
    # 自定义样式
    title_style = ParagraphStyle(
        'CustomTitle',
        parent=styles['Heading1'],
        fontSize=20,
        textColor=colors.HexColor('#000000'),
        spaceAfter=12,
        fontName=font_name,
    )
    
    heading2_style = ParagraphStyle(
        'CustomHeading2',
        parent=styles['Heading2'],
        fontSize=14,
        textColor=colors.HexColor('#1e40af'),
        spaceAfter=10,
        spaceBefore=12,
        fontName=font_name,
    )
    
    heading3_style = ParagraphStyle(
        'CustomHeading3',
        parent=styles['Heading3'],
        fontSize=12,
        textColor=colors.HexColor('#2563eb'),
        spaceAfter=6,
        spaceBefore=8,
        fontName=font_name,
    )
    
    normal_style = ParagraphStyle(
        'CustomNormal',
        parent=styles['Normal'],
        fontSize=10,
        fontName=font_name,
        spaceAfter=6,
    )
    
    lines = content.split('\n')
    i = 0
    
    while i < len(lines):
        line = lines[i]
        
        # 标题 1
        if line.startswith('# '):
            text = line[2:].strip()
            elements.append(Paragraph(text, title_style))
            elements.append(Spacer(1, 0.3*cm))
            i += 1
        
        # 标题 2
        elif line.startswith('## '):
            text = line[3:].strip()
            elements.append(Paragraph(text, heading2_style))
            i += 1
        
        # 标题 3
        elif line.startswith('### '):
            text = line[4:].strip()
            elements.append(Paragraph(text, heading3_style))
            i += 1
        
        # 列表项（以 - [ ] 开头）
        elif line.strip().startswith('- ['):
            # 收集连续的列表项
            list_items = []
            while i < len(lines) and lines[i].strip().startswith('- ['):
                item_text = lines[i].strip()[4:].strip()  # 去掉 "- [ ] "
                list_items.append(item_text)
                i += 1
            
            for item in list_items:
                item_clean = re.sub(r'<[^>]+>', '', item)
                elements.append(Paragraph(f'[] {item_clean}', normal_style))
            
            elements.append(Spacer(1, 0.2*cm))
        
        # 常规列表
        elif line.strip().startswith('- '):
            list_items = []
            while i < len(lines) and lines[i].strip().startswith('- '):
                item_text = lines[i].strip()[2:].strip()
                list_items.append(item_text)
                i += 1
            
            for item in list_items:
                item_clean = re.sub(r'<[^>]+>', '', item)
                elements.append(Paragraph(f'• {item_clean}', normal_style))
            
            elements.append(Spacer(1, 0.2*cm))
        
        # 编号列表
        elif re.match(r'^\d+\. ', line.strip()):
            list_items = []
            num = 1
            while i < len(lines) and re.match(r'^\d+\. ', lines[i].strip()):
                item_text = re.sub(r'^\d+\. ', '', lines[i].strip())
                list_items.append(item_text)
                i += 1
            
            for idx, item in enumerate(list_items, 1):
                item_clean = re.sub(r'<[^>]+>', '', item)
                elements.append(Paragraph(f'{idx}. {item_clean}', normal_style))
            
            elements.append(Spacer(1, 0.2*cm))
        
        # 水平线
        elif line.strip() == '---' or line.strip() == '***':
            elements.append(Spacer(1, 0.2*cm))
            elements.append(PageBreak())
            i += 1
        
        # 空行
        elif line.strip() == '':
            i += 1
        
        # 普通段落
        else:
            para_text = line.strip()
            if para_text:
                # 处理粗体和斜体
                para_text = re.sub(r'\*\*(.+?)\*\*', r'<b>\1</b>', para_text)
                para_text = re.sub(r'\*(.+?)\*', r'<i>\1</i>', para_text)
                para_text = re.sub(r'__(.+?)__', r'<b>\1</b>', para_text)
                
                elements.append(Paragraph(para_text, normal_style))
            i += 1
    
    return elements

def md_to_pdf(md_file: str, pdf_file: str):
    """将 Markdown 文件转换为 PDF"""
    
    # 读取 Markdown 文件
    with open(md_file, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # 解析 Markdown
    elements = parse_markdown(content)
    
    # 创建 PDF
    doc = SimpleDocTemplate(
        pdf_file,
        pagesize=A4,
        rightMargin=1.5*cm,
        leftMargin=1.5*cm,
        topMargin=1.5*cm,
        bottomMargin=1.5*cm,
    )
    
    doc.build(elements)
    print(f"OK: {pdf_file}")

if __name__ == '__main__':
    md_path = Path(__file__).parent / 'docs' / 'product-optimization-suggestions.md'
    pdf_path = Path(__file__).parent / 'docs' / 'product-optimization-suggestions.pdf'
    
    if not md_path.exists():
        print(f"ERROR: {md_path}")
        sys.exit(1)
    
    md_to_pdf(str(md_path), str(pdf_path))



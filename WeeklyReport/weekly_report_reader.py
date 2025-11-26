import os
import re
import json
from pathlib import Path
import openpyxl


def parse_work_content(content):
    """
    解析工作内容，提取标题、目的、做法/过程、结果
    """
    if not content:
        return {
            "title": "",
            "purpose": "",
            "process": "",
            "result": ""
        }
    
    content_str = str(content).strip()
    lines = content_str.split('\n')
    
    title = ""
    purpose = ""
    process = ""
    result = ""
    
    current_section = "title"
    
    for line in lines:
        line = line.strip()
        if not line:
            continue
        
        # 检查是否是新的部分
        if line.startswith("目的：") or line.startswith("目的:"):
            current_section = "purpose"
            purpose = line.replace("目的：", "").replace("目的:", "").strip()
        elif line.startswith("做法：") or line.startswith("做法:") or line.startswith("过程：") or line.startswith("过程:"):
            current_section = "process"
            process_text = line.replace("做法：", "").replace("做法:", "").replace("过程：", "").replace("过程:", "").strip()
            if process_text:
                process = process_text
        elif line.startswith("结果：") or line.startswith("结果:"):
            current_section = "result"
            result = line.replace("结果：", "").replace("结果:", "").strip()
        else:
            # 根据当前部分添加内容
            if current_section == "title":
                if title:
                    title += " " + line
                else:
                    title = line
            elif current_section == "purpose":
                if purpose:
                    purpose += " " + line
                else:
                    purpose = line
            elif current_section == "process":
                if process:
                    process += " " + line
                else:
                    process = line
            elif current_section == "result":
                if result:
                    result += " " + line
                else:
                    result = line
    
    return {
        "title": title,
        "purpose": purpose,
        "process": process,
        "result": result
    }


def extract_date_range(date_string):
    """
    从日期字符串中提取开始日期和结束日期
    例如: "工作周报        日期：2024年12月02日-2024年12月06日（第48周）" 
    """
    if not date_string:
        return None, None
    
    # 转换为字符串并清理
    date_str = str(date_string).strip()
    
    # 匹配格式: 2024年12月02日-2024年12月06日 或 2024年12月02日~2024年12月06日
    match = re.search(r'(\d{4}年\d{1,2}月\d{1,2}日)\s*[-~]\s*(\d{4}年\d{1,2}月\d{1,2}日)', date_str)
    if match:
        return match.group(1), match.group(2)
    
    return None, None


def read_weekly_report(file_path):
    """
    读取单个周报Excel文件并提取信息
    返回包含日期和工作内容的字典
    """
    print(f"\n{'='*80}")
    print(f"文件: {os.path.basename(file_path)}")
    print(f"{'='*80}")
    
    result = {
        "start_date": None,
        "end_date": None,
        "work_items": []
    }
    
    try:
        # 打开Excel文件
        workbook = openpyxl.load_workbook(file_path, data_only=True)
        sheet = workbook.active
        
        # 1. 提取日期（第1行）
        date_cell = sheet.cell(row=1, column=1).value
        if date_cell:
            start_date, end_date = extract_date_range(str(date_cell))
            result["start_date"] = start_date
            result["end_date"] = end_date
            print(f"\n开始日期: {start_date}")
            print(f"结束日期: {end_date}")
        
        # 2. 提取主要工作内容（从第5行开始）
        print(f"\n主要工作内容:")
        print(f"{'-'*80}")
        
        row_num = 5
        while True:
            # 读取A列的序号
            serial_number = sheet.cell(row=row_num, column=1).value
            
            # 如果A列没有序号，停止读取
            if not serial_number:
                break
            
            # 转换为字符串检查
            serial_str = str(serial_number).strip()
            
            # 检查序号是否包含"下周工作计划"等停止词
            if "下周" in serial_str or "工作计划" in serial_str:
                break
            
            # 尝试转换为数字，如果不是数字也停止
            try:
                int(serial_str)
            except (ValueError, TypeError):
                # 不是数字序号，可能是其他内容，停止读取
                break
            
            # 读取B到F列，找到有内容的那一列
            work_content = None
            for col in range(2, 7):  # B列(2)到F列(6)
                cell_value = sheet.cell(row=row_num, column=col).value
                if cell_value and str(cell_value).strip():
                    work_content = cell_value
                    break
            
            # 如果没有找到工作内容，跳过这行
            if not work_content:
                row_num += 1
                continue
            
            # 检查内容中是否包含"下周工作计划"
            if "下周工作计划" in str(work_content):
                break
            
            # 解析工作内容
            parsed_content = parse_work_content(work_content)
            
            result["work_items"].append(parsed_content)
            
            # 输出当前行内容
            print(f"标题: {parsed_content['title']}")
            if parsed_content['purpose']:
                print(f"  目的: {parsed_content['purpose'][:80]}...")
            if parsed_content['process']:
                print(f"  做法: {parsed_content['process'][:80]}...")
            if parsed_content['result']:
                print(f"  结果: {parsed_content['result'][:80]}...")
            print()
            
            row_num += 1
            
            # 安全检查：避免无限循环
            if row_num > 1000:
                break
        
        workbook.close()
        
    except Exception as e:
        print(f"读取文件时出错: {e}")
    
    return result


def main():
    """
    主函数：读取指定路径下的所有Excel文件
    """
    target_path = r"D:\浦林城建\人事与绩效\周报\读取"
    
    print(f"正在读取路径: {target_path}")
    
    # 检查路径是否存在
    if not os.path.exists(target_path):
        print(f"错误: 路径不存在 - {target_path}")
        return
    
    # 获取所有Excel文件
    excel_files = []
    for file in os.listdir(target_path):
        if file.endswith(('.xlsx', '.xls')) and not file.startswith('~$'):
            excel_files.append(os.path.join(target_path, file))
    
    if not excel_files:
        print("未找到Excel文件")
        return
    
    print(f"找到 {len(excel_files)} 个Excel文件\n")
    
    # 存储所有文件的结果
    all_results = []
    
    # 逐个读取并处理Excel文件
    for file_path in sorted(excel_files):
        file_result = read_weekly_report(file_path)
        all_results.append(file_result)
    
    # 输出JSON数组
    print(f"\n\n{'='*80}")
    print("JSON输出:")
    print(f"{'='*80}")
    json_output = json.dumps(all_results, ensure_ascii=False, indent=2)
    print(json_output)
    
    # 可选：保存到文件
    output_file = os.path.join(target_path, "weekly_reports.json")
    with open(output_file, 'w', encoding='utf-8') as f:
        f.write(json_output)
    print(f"\nJSON数据已保存到: {output_file}")


if __name__ == "__main__":
    main()

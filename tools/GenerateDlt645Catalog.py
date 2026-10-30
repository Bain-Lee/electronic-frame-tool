"""Generate the 645 catalog from the supplied PDF; Assistent is comparison only."""
import argparse
import copy
import csv
import itertools
import json
import re
from collections import Counter
from pathlib import Path
import xml.etree.ElementTree as ET

import pdfplumber


ROOT = Path(__file__).resolve().parents[1]
ENERGY = ['组合有功', '正向有功', '反向有功', '组合无功1', '组合无功2',
          '第一象限无功', '第二象限无功', '第三象限无功', '第四象限无功', '正向视在', '反向视在']


def clean(text):
    return re.sub(r'\s+', '', text or '').replace('（', '(').replace('）', ')')


def slot(text):
    tokens = re.findall(r'(?<![0-9A-Za-z])(?:[0-9A-F]{2}|ZZ)(?![0-9A-Za-z])|…+', text)
    values = []
    for index, token in enumerate(tokens):
        if token.startswith('…') and index > 0 and index + 1 < len(tokens):
            if re.fullmatch('[0-9A-F]{2}', tokens[index - 1]) and re.fullmatch('[0-9A-F]{2}', tokens[index + 1]):
                values.extend(range(int(tokens[index - 1], 16) + 1, int(tokens[index + 1], 16)))
        elif re.fullmatch('[0-9A-F]{2}', token):
            values.append(int(token, 16))
    return list(dict.fromkeys(values))


def element(parent, name, text=None, **attributes):
    node = ET.SubElement(parent, name, {key: str(value) for key, value in attributes.items()})
    if text is not None:
        node.text = str(text)
    return node


def section(category):
    return 'A.' + str({0: 1, 1: 2, 2: 3, 3: 4, 4: 5, 5: 6, 6: 7}.get(category, 4))


def names_in_cell(text):
    lines = [clean(line) for line in text.splitlines() if clean(line) and not clean(line).startswith('…')]
    starts = list(re.finditer(r'[(（](?:当前|上\s*\d+\s*(?:结算日|次))\s*[)）]', text))
    if len(starts) > 1:
        return [clean(text[match.start(): starts[index + 1].start() if index + 1 < len(starts) else len(text)]).strip('…')
                for index, match in enumerate(starts)]
    return lines


def indexed(text, index, count):
    values = [clean(line) for line in text.splitlines() if clean(line) and not clean(line).startswith('…')]
    return values[index] if len(values) == count else values[0] if len(set(values)) == 1 else ''


def time_format(value):
    return {'YYMMDDhhmmss': 'ssmmhhDDMMYY', 'YYMMDDhhmm': 'mmhhDDMMYY',
            'YYMMDDWW': 'WWDDMMYY', 'YYMMDD': 'DDMMYY', 'hhmmss': 'ssmmhh',
            'hhmm': 'mmhh', 'MMDDhhmm': 'mmhhDDMM', 'DDhh': 'hhDD'}.get(value)


def scalar(parent, name, length, fmt, unit='', signed=False):
    node = element(parent, 'splitByLength')
    element(node, 'name', name)
    element(node, 'length', length)
    clock = time_format(fmt)
    if clock:
        element(node, 'type', 'BCD')
        element(node, 'time', clock)
    elif re.fullmatch(r'[XN]+(?:\.[XN]+)?', fmt or '') and len(fmt.replace('.', '')) == length * 2:
        element(node, 'type', 'BCD')
        element(node, 'decimal', len(fmt.split('.')[1]) if '.' in fmt else 0)
        if unit:
            element(node, 'unit', unit)
        if signed:
            element(node, 'sign', 'yes')
    else:
        element(node, 'type', 'RAW')
    return node


def set_scalar(item, length, fmt, unit='', signed=False):
    for child in list(item):
        if child.tag not in ['name', 'sourceRef', 'note', 'request']:
            item.remove(child)
    item.set('status', 'ready')
    field = scalar(ET.Element('temporary'), item.findtext('name'), length, fmt, unit, signed)
    for child in list(field):
        if child.tag != 'name':
            item.append(child)


def set_repeat(item, length, fmt, unit='', demand=False, signed=False):
    for child in list(item):
        if child.tag not in ['name', 'sourceRef', 'note', 'request']:
            item.remove(child)
    item.set('status', 'ready')
    element(item, 'length', 'unknown')
    repeat = element(item, 'repeat', count='remaining', itemLength=length, minimum=1, maximum=64, first='总量')
    element(repeat, 'name', '费率')
    scalar(repeat, '最大需量' if demand else '电能', 3 if demand else length, 'XX.XXXX' if demand else fmt, unit, signed)
    if demand:
        scalar(repeat, '发生时间', 5, 'YYMMDDhhmm')


def set_bits(item, length, fields):
    set_scalar(item, length, '')
    identifier = item.get('id')
    page = 112 if identifier.startswith('040009') else 110 if identifier.startswith('040005') and int(identifier[-2:], 16) <= 4 else 111
    element(item, 'sourceRef', page=page, section='附录C位域')
    bits = element(item, 'splitbit')
    for bit, name, choices in fields:
        node = element(bits, 'bit', id=bit)
        element(node, 'name', name)
        for key, label in enumerate(choices):
            element(node, 'value', label, key=key)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--standard', type=Path, default=ROOT / 'specifications/3-DLT645-2007.pdf')
    parser.add_argument('--reference', type=Path)
    parser.add_argument('--evidence', type=Path, default=ROOT / '.work/dlt645-alignment')
    args = parser.parse_args()
    args.evidence.mkdir(parents=True, exist_ok=True)
    rows = []
    previous = None
    with pdfplumber.open(args.standard) as document:
        for page_number in range(23, 108):
            page = document.pages[page_number - 1]
            for table_number, table in enumerate(page.find_tables(), 1):
                extracted = table.extract()
                if not extracted or len(extracted[0]) not in (10, 11):
                    continue
                for row_number, cells in enumerate(extracted, 1):
                    cells = [value or '' for value in cells]
                    if re.fullmatch('[0-9A-F]{2}', cells[0].strip()):
                        previous = {'page': page_number, 'table': table_number, 'row': row_number, 'cells': cells, 'access': {}}
                        variable = [index for index in range(4) if len(slot(cells[index])) > 1]
                        if len(variable) == 1 and '…' not in cells[variable[0]]:
                            column = variable[0]
                            box = table.rows[row_number - 1].cells[column]
                            if box:
                                codes = [(word['text'], (word['top'] + word['bottom']) / 2) for word in page.crop(box).extract_words()
                                         if re.fullmatch('[0-9A-F]{2}', word['text'])]
                                unit_box = table.rows[row_number - 1].cells[6]
                                units = {code: [] for code, _ in codes}
                                if unit_box:
                                    for word in page.crop(unit_box).extract_words():
                                        nearest = min(codes, key=lambda code: abs(code[1] - (word['top'] + word['bottom']) / 2))[0]
                                        units[nearest].append(word['text'])
                                previous['units'] = {'column': column, 'values': {code: ''.join(words) for code, words in units.items()}}
                                for function_column in [7, 8]:
                                    access_box = table.rows[row_number - 1].cells[function_column]
                                    stars = [] if not access_box else [word for word in page.crop(access_box).extract_words() if word['text'] == '*']
                                    if len(stars) > 1:
                                        marked = {min(codes, key=lambda code: abs(code[1] - (word['top'] + word['bottom']) / 2))[0] for word in stars}
                                        previous['access'][str(function_column)] = {'column': column, 'codes': sorted(marked)}
                        rows.append(previous)
                    elif not cells[0].strip() and previous and len(previous['cells']) == len(cells):
                        for function_column in [7, 8]:
                            mapping = previous['access'].get(str(function_column))
                            if mapping and slot(cells[mapping['column']]):
                                box = table.rows[row_number - 1].cells[mapping['column']]
                                codes = [(word['text'], (word['top'] + word['bottom']) / 2) for word in page.crop(box).extract_words()
                                         if re.fullmatch('[0-9A-F]{2}', word['text'])]
                                access_box = table.rows[row_number - 1].cells[function_column]
                                stars = [] if not access_box else [word for word in page.crop(access_box).extract_words() if word['text'] == '*']
                                mapping['codes'] = sorted(set(mapping['codes']) | {
                                    min(codes, key=lambda code: abs(code[1] - (word['top'] + word['bottom']) / 2))[0] for word in stars})
                        previous.setdefault('continuations', []).append({'page': page_number, 'table': table_number, 'row': row_number})
                        for index, value in enumerate(cells):
                            if value:
                                previous['cells'][index] += '\n' + value
    (args.evidence / 'source-rows.json').write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding='utf-8')
    root = ET.Element('config', protocol='dlt645', version='DL/T 645-2007', schema='FrameTools-1.1')
    element(root, 'source', title='多功能电能表通信协议', file=args.standard.name)
    element(root, 'note', '按本机PDF的附录A、B、C整理；只发布数据定义及页码引用。raw结构不猜测解码，范围不按厂商配置扩大。')
    frame = element(root, 'frame', start='68', end='16', checksum='从首个68至数据域末尾累加模256')
    element(frame, 'layout', '[FE] 68 A(6) 68 C L DI/DATA(+33H) CS 16')
    items = {}
    unresolved = []

    def make(identifier, name, row, access='r', status='raw'):
        item = ET.Element('dataItem', id=identifier, protocol='dlt645', function=','.join(['11'] if access == 'r' else ['14'] if access == 'w' else ['11', '14']),
                          dir='0,1', access=access, status=status, source='DL/T 645-2007 附录' + section(int(identifier[:2], 16)))
        element(item, 'name', name)
        element(item, 'sourceRef', page=row['page'], table=row['table'], row=row['row'])
        for continuation in row.get('continuations', []):
            element(item, 'sourceRef', **continuation)
        element(item, 'length', 'unknown')
        items.setdefault(identifier, item)
        return items[identifier]

    for row in rows:
        cells = row['cells']
        slots = [slot(value) for value in cells[:4]]
        if any(not values for values in slots) or any('ZZ' in value for value in cells[:4]):
            unresolved.append(row)
            continue
        if any(value.strip().endswith('…') for value in cells[:4]):
            unresolved.append(row)
        variations = [index for index, values in enumerate(slots) if len(values) > 1]
        names = names_in_cell(cells[9])
        for code in itertools.product(*slots):
            identifier = ''.join('%02X' % value for value in code)
            access = ''
            for function_column, flag in [(7, 'r'), (8, 'w')]:
                mapping = row['access'].get(str(function_column))
                enabled = ('%02X' % code[mapping['column']]) in mapping['codes'] if mapping else '*' in cells[function_column]
                if enabled:
                    access += flag
            if not access:
                access = 'r'
            index = slots[variations[0]].index(code[variations[0]]) if len(variations) == 1 else 0
            count = len(slots[variations[0]]) if len(variations) == 1 else 1
            name = names[index] if len(names) == count else names[0] if names else '数据项' + identifier
            if len(names) != count and len(variations) == 1:
                first = slots[variations[0]][0]
                number = code[variations[0]]
                if '…' in cells[9] and not re.search(r'[(（](?:当前|上)', cells[9]):
                    name = clean(cells[9].split('…')[0])
                name = re.sub(r'上\s*' + str(first) + r'(?=次|月)', '上' + str(number), name)
                name = re.sub(r'第\s*' + str(first) + r'(?=\D)', '第' + str(number), name)
                name = re.sub(r'(电价)' + str(first) + r'$', r'\g<1>' + str(number), name)
                if number != first and ('数据块' in names[-1]) and number == 255:
                    name = names[-1]
            name = name.split('：')[0].rstrip(':')
            item = make(identifier, name, row, access)
            fmt = indexed(cells[4], index, count)
            length = indexed(cells[5], index, count)
            unit = indexed(cells[6], index, count)
            if not unit and 'units' in row:
                unit = row['units']['values'].get('%02X' % code[row['units']['column']], unit)
            if re.fullmatch(r'\d+', length or '') and len(variations) <= 1:
                length = int(length)
                if time_format(fmt) or re.fullmatch(r'[XN]+(?:\.[XN]+)?', fmt or '') and len(fmt.replace('.', '')) == length * 2:
                    set_scalar(item, length, fmt, unit)
                elif 'ASCII' in name or 'ASCII' in cells[9] and count == 1:
                    set_scalar(item, length, 'RAW')
                    item.find('type').text = 'ASCII'
            if '数据块' in name:
                item.set('status', 'raw')
            if code[0] in [6, 7]:
                item.set('status', 'raw')

    # A.1/A.2 list settlement days 00, 01 and 0C with an explicit ellipsis.
    for category in [0, 1]:
        settlement_prefixes = {identifier[:6] for identifier in items
                               if identifier.startswith('%02X' % category) and identifier.endswith('0C')}
        # A.1's A-phase supplemental row has blank merged DI2/DI1/DI0 cells,
        # but explicitly names the twelfth settlement day.
        if category == 0:
            for row in unresolved:
                cells = row['cells']
                if cells[0] == '00' and not cells[2].strip() and '(上12结算日)' in clean(cells[9]):
                    settlement_prefixes.update('00%02X00' % value for value in slot(cells[1]))
        current = [(identifier, copy.deepcopy(item)) for identifier, item in list(items.items())
                   if identifier[:6] in settlement_prefixes and identifier.endswith('00')]
        for identifier, original in current:
            for period in range(13):
                target = identifier[:6] + '%02X' % period
                item = copy.deepcopy(original)
                item.set('id', target)
                if target in items:
                    item.find('sourceRef').attrib = dict(items[target].find('sourceRef').attrib)
                item.find('name').text = clean(original.findtext('name')).replace('(当前)', '当前' if period == 0 else '上%d结算日' % period)
                item.set('source', 'DL/T 645-2007 表A.%d，DI0=00至0C' % (category + 1))
                items[target] = item
                parts = bytes.fromhex(target)
                if parts[1] <= 10:
                    prefix = '当前' if period == 0 else '上%d结算日' % period
                    base = ENERGY[parts[1]]
                    if parts[2] == 255:
                        item.find('name').text = prefix + base + ('电能数据块' if category == 0 else '最大需量及发生时间数据块')
                    else:
                        tariff = '总' if parts[2] == 0 else '费率%d' % parts[2]
                        item.find('name').text = prefix + base + tariff + ('电能' if category == 0 else '最大需量及发生时间')
                name = item.findtext('name')
                signed = '组合' in name and (category == 0 or '无功' in name)
                unit = item.findtext('unit') or ('kWh' if parts[1] < 3 else 'kvarh' if parts[1] < 9 else 'kVAh')
                if category == 0:
                    if parts[2] == 255:
                        set_repeat(item, 4, 'XXXXXX.XX', unit, signed=signed)
                    else:
                        set_scalar(item, 4, 'XXXXXX.XX', unit, signed)
                else:
                    unit = ('kvar' if 3 <= parts[1] <= 8 else 'kVA' if parts[1] in [9, 10, 0x1D, 0x1E, 0x31, 0x32, 0x45, 0x46] else 'kW')
                    if parts[2] == 255:
                        set_repeat(item, 8, '', unit, demand=True, signed=signed)
                    else:
                        for child in list(item):
                            if child.tag not in ['name', 'sourceRef', 'note']:
                                item.remove(child)
                        item.set('status', 'ready')
                        element(item, 'length', 8)
                        scalar(item, '最大需量', 3, 'XX.XXXX', unit, signed)
                        scalar(item, '发生时间', 5, 'YYMMDDhhmm')
        for identifier, source in list(items.items()):
            if identifier[:6] in settlement_prefixes and identifier.endswith('00') and identifier[4:6] != 'FF':
                item = copy.deepcopy(source)
                item.set('id', identifier[:6] + 'FF')
                item.find('name').text = source.findtext('name').replace('当前', '当前及上12结算日') + '数据块'
                for child in list(item):
                    if child.tag not in ['name', 'sourceRef', 'note']:
                        item.remove(child)
                element(item, 'length', 52 if category == 0 else 104)
                for period in range(13):
                    child_source = items[identifier[:6] + '%02X' % period]
                    group = element(item, 'splitByLength')
                    for child in child_source:
                        if child.tag in ['name', 'length', 'type', 'decimal', 'unit', 'sign', 'time', 'splitByLength']:
                            group.append(copy.deepcopy(child))
                items[item.get('id')] = item

    # The A.3 signed quantities are specified by notes 2, 3 and 6.
    for identifier, item in list(items.items()):
        code = bytes.fromhex(identifier)
        if code[0] == 2 and code[1] in [10, 11] and 1 <= code[2] <= 3:
            item.find('name').text = 'ABC'[code[2] - 1] + '相' + ('电压' if code[1] == 10 else '电流') + (str(code[3]) + '次谐波含量' if code[3] != 255 else '谐波含量数据块')
        if code[0] == 2 and code[2] != 255 and code[3] != 255:
            if code[1] in [2, 3, 4, 5, 6] or code[1] == 0x80 and code[3] in [1, 3, 4, 5, 6, 7]:
                if item.find('type') is not None and item.findtext('type') == 'BCD':
                    element(item, 'sign', 'yes')
        if code[0] == 2 and ('数据块' in item.findtext('name')):
            sources = [value for key, value in sorted(items.items()) if key.startswith(identifier[:4])
                       and (key[6:] == identifier[6:] if code[2] == 255 else key[4:6] == identifier[4:6])
                       and key != identifier and value.get('status') == 'ready' and value.find('repeat') is None]
            if code[2] == 255:
                sources = [value for value in sources if value.get('id')[6:] == '00']
            elif code[3] == 255:
                sources = [value for value in sources if 1 <= int(value.get('id')[6:], 16) <= 21]
            if sources:
                for child in list(item):
                    if child.tag not in ['name', 'sourceRef', 'note']:
                        item.remove(child)
                item.set('status', 'ready')
                element(item, 'length', sum(int(value.findtext('length')) for value in sources))
                for source in sources:
                    group = element(item, 'splitByLength')
                    for child in source:
                        if child.tag in ['name', 'length', 'type', 'decimal', 'unit', 'sign', 'time']:
                            group.append(copy.deepcopy(child))

    # A.6: n is the actual frozen tariff count plus the total, never a fixed 64.
    maxima = {0: 12, 1: 3, 2: 2, 3: 2, 4: 254, 6: 62, 7: 2}
    for identifier, original in list(items.items()):
        code = bytes.fromhex(identifier)
        if code[0] != 5 or code[1] not in maxima or code[3] != 1:
            continue
        for record in range(1, maxima[code[1]] + 1):
            item = copy.deepcopy(original)
            item.set('id', identifier[:6] + '%02X' % record)
            item.find('name').text = re.sub(r'上1次', '上%d次' % record, item.findtext('name'))
            if 1 <= code[2] <= 8 and code[1] != 4:
                set_repeat(item, 4, 'XXXXXX.XX', 'kWh' if code[2] <= 2 else 'kvarh', signed=code[2] in [3, 4])
            elif code[2] in [9, 10] and code[1] != 4:
                set_repeat(item, 8, '', 'kW', demand=True)
            elif code[2] == 0:
                set_scalar(item, 5, 'YYMMDDhhmm')
            elif code[1] == 4 and code[2] in [1, 2]:
                set_scalar(item, 4, 'XXXXXX.XX', 'kWh')
            items[item.get('id')] = item

    # A.4 purchase records explicitly span record 1 through record 10.
    for identifier, original in list(items.items()):
        if identifier.startswith(('0332', '0333')) and identifier.endswith('01'):
            for record in range(2, 11):
                item = copy.deepcopy(original)
                item.set('id', identifier[:6] + '%02X' % record)
                item.find('name').text = item.findtext('name').replace('上1次', '上%d次' % record)
                items.setdefault(item.get('id'), item)

    # The supplied supplemental event tables use ZZ for the fields listed in
    # their record-1 rows, including explicitly stated "same as" families.
    inherited = {0x11: 0x10, 0x12: 0x10, 0x13: 0x10, 0x15: 0x14, 0x17: 0x16,
                 0x19: 0x18, 0x1A: 0x18, 0x1C: 0x1B}
    event_names = {0x10: '失压', 0x11: '欠压', 0x12: '过压', 0x13: '断相', 0x14: '电压逆相序',
                   0x15: '电流逆相序', 0x16: '电压不平衡', 0x17: '电流不平衡', 0x18: '失流',
                   0x19: '过流', 0x1A: '断流', 0x1B: '潮流反向', 0x1C: '过载', 0x1D: '跳闸', 0x1E: '合闸'}
    for row in unresolved:
        cells = row['cells']
        category = int(cells[0], 16)
        if category < 0x10 or 'ZZ' not in cells[2]:
            continue
        source_category = inherited.get(category, category)
        for peer in slot(cells[1]):
            sources = [(key, value) for key, value in list(items.items())
                       if key.startswith('%02X%02X' % (source_category, peer)) and key.endswith('01') and key[4:6] != '00']
            for key, original in sources:
                for record in slot(cells[3]):
                    identifier = '%02X%02X' % (category, peer) + key[4:6] + '%02X' % record
                    if identifier in items:
                        continue
                    item = copy.deepcopy(original)
                    item.set('id', identifier)
                    item.set('status', 'raw')
                    item.find('sourceRef').attrib = {'page': str(row['page']), 'table': str(row['table']), 'row': str(row['row'])}
                    name = item.findtext('name')
                    if category != source_category:
                        name = name.replace(event_names[source_category], event_names[category])
                    item.find('name').text = re.sub(r'上1次', '上%d次' % record, name) if record != 255 else name + '的10次记录数据块'
                    items[identifier] = item

    for identifier, item in items.items():
        name = item.findtext('name')
        if identifier.startswith('00') and '组合' in name and item.findtext('type') == 'BCD' and item.find('sign') is None:
            element(item, 'sign', 'yes')
        if identifier.startswith('040004'):
            if 'ASCII' in name:
                if item.find('type') is not None:
                    item.set('status', 'ready')
                    item.find('type').text = 'ASCII'
            elif '地址' in name or '表号' in name or '客户编号' in name:
                if item.find('type') is not None:
                    item.find('type').text = 'RAW'
        if identifier.startswith('04000C'):
            item.find('name').text = '%d级密码' % (int(identifier[-2:], 16) - 1)
        if identifier in ['04001101', '04001301']:
            item.find('type').text = 'RAW'
            element(item, 'note', '保留特征/指示字原始位值，不按BCD数值解释。')
        if identifier in ['04000306', '04000307']:
            item.set('status', 'ambiguous')
            element(item, 'note', '本机表A.5将06与07均命名为电流互感器变比，未自行将其中一项改成电压互感器变比。')
        if identifier in ['00900200', '00900201']:
            item.set('status', 'ambiguous')
            element(item, 'note', '本机表A.1名称写为剩余/透支电量，但单位列为元；保留原文，未自行改称金额。')
        if identifier == '04000101':
            item.find('name').text = '日期及星期'
        if identifier in ['04000B01', '04000B02', '04000B03']:
            element(item, 'value', '未设置结算日', key=9999)
        if identifier.startswith('06'):
            kind = {'00': 'count', '01': 'time-count', '02': 'recent'}.get(identifier[-2:])
            if kind:
                element(item, 'request', kind=kind, length=6 if kind == 'time-count' else 1, source='表A.7主站下行格式；响应参见附录B')

    # Appendix C defines these bytes as bit fields, even where A.5 prints NN.
    set_bits(items['04000501'], 2, [
        (0, '外置开关控制方式', ['电平', '脉冲']), (1, '需量积算方式', ['滑差', '区间']),
        (2, '时钟电池', ['正常', '欠压']), (3, '停电抄表电池', ['正常', '欠压']),
        (4, '有功功率方向', ['正向', '反向']), (5, '无功功率方向', ['正向', '反向'])])
    set_bits(items['04000502'], 2, [(bit, phase + kind + '功率方向', ['正向', '反向'])
                                  for base, kind in [(0, '有功'), (4, '无功')]
                                  for bit, phase in enumerate('ABC', base)])
    set_bits(items['04000503'], 2, [
        (0, '当前运行时段', ['第一套', '第二套']), ('1-2', '供电方式', ['主电源', '辅助电源', '电池供电']),
        (3, '编程允许', ['禁止', '许可']), (4, '继电器状态', ['通', '断']),
        (5, '当前运行时区', ['第一套', '第二套']), (6, '继电器命令状态', ['通', '断']),
        (7, '预跳闸报警状态', ['无', '有']), ('8-9', '电能表类型', ['非预付费表', '电量型预付费表', '电费型预付费表']),
        (10, '当前运行费率电价', ['第一套', '第二套']), (11, '当前阶梯', ['第一套', '第二套'])])
    for number in range(4, 8):
        names = ['失压', '欠压', '过压', '失流', '过流', '过载', '潮流反向', '断相', '断流'] if number < 7 else [
            '电压逆相序', '电流逆相序', '电压不平衡', '电流不平衡', '辅助电源失电', '掉电', '需量超限', '总功率因数超下限', '电流严重不平衡']
        set_bits(items['040005%02X' % number], 2, [(bit, name, ['无', '发生']) for bit, name in enumerate(names)])
    status_block = items['040005FF']
    status_block.set('status', 'ready')
    status_block.find('length').text = '14'
    for tag in ['type', 'decimal']:
        child = status_block.find(tag)
        if child is not None:
            status_block.remove(child)
    for number in range(1, 8):
        group = element(status_block, 'splitByLength')
        for child in items['040005%02X' % number]:
            if child.tag in ['name', 'length', 'type', 'splitbit']:
                group.append(copy.deepcopy(child))
    for number in range(1, 4):
        names = ['正向有功', '反向有功'] if number == 1 else ['I象限', 'II象限', 'III象限', 'IV象限']
        set_bits(items['040006%02X' % number], 1,
                 [(index * 2 + action, name + suffix, choices)
                  for index, name in enumerate(names)
                  for action, suffix, choices in [(0, '加', ['不加', '加']), (1, '减', ['不减', '减'])]])
    for number in range(1, 6):
        set_bits(items['040007%02X' % number], 1, [(bit, str(rate) + 'bps', ['非当前速率', '当前速率'])
                                                for bit, rate in enumerate([600, 1200, 2400, 4800, 9600, 19200], 1)])
    set_bits(items['04000801'], 1, [(bit, '周' + day, ['休息', '工作']) for bit, day in enumerate('日一二三四五六')])
    for number in range(1, 7):
        names = ['电压、电流、频率', '有、无功功率', '功率因数', '有、无功总电能', '四象限无功总电能', '当前需量'] if number == 1 else [
            '正向有功电能', '反向有功电能', '组合无功1电能', '组合无功2电能', '四象限无功电能', '正向有功最大需量及发生时间', '反向有功最大需量及发生时间', '变量']
        if number == 5:
            names = ['正向有功总电能', '反向有功总电能']
        set_bits(items['040009%02X' % number], 1, [(bit, name, ['不记录', '记录']) for bit, name in enumerate(names)])

    # Load-record classes 2..5 are explicitly elided between class 1 and class 6.
    for kind in range(7):
        for query in range(3):
            identifier = '06%02X00%02X' % (kind, query)
            if identifier not in items:
                sample = copy.deepcopy(items['060100%02X' % query])
                sample.set('id', identifier)
                sample.find('name').text = sample.findtext('name').replace('第1类', '第%d类' % kind)
                items[identifier] = sample

    for item in sorted(items.values(), key=lambda item: item.get('id')):
        root.append(item)
    ranges = []
    for row in unresolved:
        if all(value.strip().startswith('…') for value in row['cells'][1:4]):
            continue
        ranges.append(row)
        node = element(root, 'dataRange', pattern=' '.join(clean(value) for value in row['cells'][:4]), status='raw')
        element(node, 'name', clean(row['cells'][9]))
        element(node, 'sourceRef', page=row['page'], table=row['table'], row=row['row'])
    issues = element(root, 'issues')
    element(issues, 'issue', '开放末端的阶梯范围、ZZ关联范围及复合记录仅在来源能确定具体编码时展开；未实现结构保持raw。')
    element(issues, 'issue', '表A.5两项互感器变比名称重复，表A.1两项电量名称与元单位不一致；保留原文。安全补充表及负荷响应不按普通BCD值解释。')
    ET.indent(root, space='    ')
    destination = ROOT / 'protocols/DLT645.xml'
    ET.ElementTree(root).write(destination, encoding='utf-8', xml_declaration=True)
    with destination.open('a', encoding='utf-8') as stream:
        stream.write('\n')
    summary = {'source': args.standard.name, 'sourceRows': len(rows), 'definitions': len(items),
               'status': dict(Counter(item.get('status') for item in items.values())),
               'categories': dict(Counter(identifier[:2] for identifier in items)), 'rangePatterns': len(ranges)}
    if args.reference:
        reference = ET.parse(args.reference).getroot()
        references = {}
        invalid = []
        for item in reference.iter('dataItem'):
            identifier = item.get('id', '').upper()
            if not re.fullmatch('[0-9A-F]{8}', identifier):
                invalid.append(identifier)
            else:
                references.setdefault(identifier, item)
        differences = []
        for identifier, item in sorted(items.items()):
            old = references.get(identifier)
            if old is None:
                differences.append([identifier, '参考缺失', item.findtext('name'), item.findtext('length'), ''])
            elif old.findtext('length') != item.findtext('length'):
                differences.append([identifier, '长度不同', item.findtext('name'), item.findtext('length'), old.findtext('length')])
        with (args.evidence / 'reference-differences.csv').open('w', encoding='utf-8-sig', newline='') as stream:
            writer = csv.writer(stream)
            writer.writerow(['DI', '差异', '标准名称', '本版长度', '参考长度'])
            writer.writerows(differences)
        summary['referenceUnique'] = len(references)
        summary['referenceDifferences'] = len(differences)
        summary['referenceInvalidIdentifiers'] = len(set(invalid))
        summary['referenceNotInGeneratedSet'] = len(set(references) - set(items))
    (args.evidence / 'generation-summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()

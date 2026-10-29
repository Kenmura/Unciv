import sys,os,xml.etree.ElementTree as ET
def defs(files):
    d={}
    for f in files:
        r=ET.parse(f).getroot()
        for tag in ('Defines','PostDefines'):
            for tb in r.iter(tag):
                for row in tb.iter('Row'):
                    n=row.get('Name') or (row.find('Name').text if row.find('Name') is not None else None)
                    v=row.find('Value'); k=row.find('Key')
                    val=(v.text if v is not None else None) or (k.text if k is not None else None)
                    if n: d[n]=val
                for upd in tb.iter('Update'):
                    w=upd.find('Where'); s=upd.find('Set')
                    if w is not None and s is not None: d[w.get('Name')]=s.get('Value')
    return d
a=defs(sys.argv[1].split('|')); b=defs(sys.argv[2].split('|'))
for k in b:
    if k not in a: print('NEW',k,b[k])
    elif a[k]!=b[k]: print('CHG',k,a[k],'->',b[k])

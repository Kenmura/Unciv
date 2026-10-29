# usage: fn.py <redir> str REGEX | def REGEX | show ADDR[,ADDR] | callers ADDR | callees ADDR
import re,json,sys,pickle,os
R=sys.argv[1]; cmd=sys.argv[2]; arg=sys.argv[3] if len(sys.argv)>3 else ''
d=json.load(open(R+'/defines.json')); base=d['base']; t={int(k,16):v for k,v in d['tab'].items()}
blocks=open(R+'/decomp.c',errors='replace').read().split('\n// @')
B={b.split()[0]:b for b in blocks[1:]}
def lab(b):
    b=re.sub(r'DAT_(10[0-9a-f]{6})',lambda m:'D.'+t[int(m.group(1),16)-base] if int(m.group(1),16)-base in t else m.group(0),b)
    b=re.sub(r'\n  (undefined\d?|int|uint|char|bool|float10|short|byte|float|double|ushort|longlong|longdouble|void) \*?[^\n(]*;','',b)
    b=re.sub(r'std::basic_string<char,std::char_traits<char>,std::allocator<char>_>','string',b)
    return b
def defs(b): return {t[int(a,16)-base] for a in re.findall(r'DAT_(10[0-9a-f]{6})',b) if int(a,16)-base in t}
if cmd=='str':
    p=re.compile(arg)
    for a,b in B.items():
        s=[x for x in re.findall(r'"([^"\n]{3,})"',b) if p.search(x)]
        if s: print(a,len(b),sorted(set(s))[:6])
elif cmd=='def':
    p=re.compile(arg)
    for a,b in B.items():
        n=sorted(x for x in defs(b) if p.search(x))
        if n: print(a,len(b),n[:8])
elif cmd=='show':
    for a in arg.split(','): print('// @'+lab(B[a]).strip()+'\n')
else:
    c=json.load(open(R+'/calls.json'))
    if cmd=='callees': print(c.get(arg))
    else: print([k for k,v in c.items() if arg in v])

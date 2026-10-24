import re,json,sys
# usage: label.py <expN> addr[,addr...] | find <prefix-regex>
S=sys.argv[1]; e=sys.argv[2]
d=json.load(open(f'{S}/out{e}/defines.json')); base=d['base']; t={int(k,16):v for k,v in d['tab'].items()}
blocks=open(f'{S}/out{e}/decomp.c',errors='replace').read().split('\n// @')
def lab(b):
    b=re.sub(r'DAT_(10[0-9a-f]{6})',lambda m:t.get(int(m.group(1),16)-base,m.group(0)),b)
    b=re.sub(r'\n  (undefined\d?|int|uint|char|bool|float10|short|byte|float|double|ushort|longlong) [^\n]*;','',b)
    b=re.sub(r'std::basic_string<char,std::char_traits<char>,std::allocator<char>_>','string',b)
    return b
if sys.argv[3]=='find':
    pat=re.compile(sys.argv[4]); out=[]
    for b in blocks:
        addrs={int(a,16) for a in re.findall(r'DAT_(10[0-9a-f]{6})',b)}
        names=sorted({t[a-base] for a in addrs if (a-base) in t and pat.search(t[a-base])})
        if names: out.append((len(names),b.split()[0],len(b),names))
    for n,a,l,nm in sorted(out,reverse=True)[:int(sys.argv[5]) if len(sys.argv)>5 else 15]: print(n,a,l,nm[:5])
else:
    for a in sys.argv[3].split(','):
        for b in blocks:
            if b.startswith(a): print('//',lab(b).strip(),'\n')

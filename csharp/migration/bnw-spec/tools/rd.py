# usage: rd.py <dll> f|d|i ADDR...  read float/double/int at virtual address
import sys,struct
b=open(sys.argv[1],'rb').read(); pe=struct.unpack_from('<I',b,0x3c)[0]
ns=struct.unpack_from('<H',b,pe+6)[0]; osz=struct.unpack_from('<H',b,pe+20)[0]
ib=struct.unpack_from('<I',b,pe+24+28)[0]; so=pe+24+osz
secs=[struct.unpack_from('<8sIIII',b,so+i*40) for i in range(ns)]
def off(va):
    r=va-ib
    for n,vs,vaddr,rs,ro in secs:
        if vaddr<=r<vaddr+max(vs,rs): return ro+r-vaddr
k=sys.argv[2]
for a in sys.argv[3:]:
    o=off(int(a,16)); fmt={'f':'<f','d':'<d','i':'<i'}[k]
    print(a, struct.unpack_from(fmt,b,o)[0])

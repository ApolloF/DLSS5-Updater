from ext import *
import collections
def find(name):
    b=B(name+'.bin')
    off=[int(l.split()[0],16) for l in open(name+'.a.txt') if l.rstrip().endswith(' RenoDX.DLSS5') or l.split()[1:]==['RenoDX.DLSS5']]
    return b,[b.off2rva(o) for o in off]
if __name__=='__main__':
    n=sys.argv[1]
    b,rv=find(n); print([hex(x) for x in rv])
    r=xrefs(b,rv) if False else None

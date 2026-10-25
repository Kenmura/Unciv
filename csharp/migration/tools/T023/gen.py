import collections,sys
def hx(s): return ''.join(chr(int(h,16)) for h in s.split(',')) if s else ''
def esc(s): return ''.join(c if (0x20<=ord(c)<0x7f and c not in '"\\') else '\\u%04X'%ord(c) for c in s)
def chesc(c): return "'"+('\\\'' if c=="'" else esc(c))+"'"
rows=[l.rstrip('\n').split('\t') for l in open('num.tsv',encoding='utf-8')]
net={}
for l in open('net_case.tsv'):
    c,u,lo=l.split(); net[int(c,16)]=(chr(int(u,16)),chr(int(lo,16)))
per=collections.defaultdict(dict)
for l in open('case.tsv',encoding='utf-8'):
    lang,c,u,lo=l.rstrip('\n').split('\t'); per[lang][int(c,16)]=(hx(u),hx(lo))
out=[]
w=out.append
w('// Generated from the JVM (OpenJDK 25, CLDR locale data) with the Unciv.jar LocaleCode / java.text.DecimalFormat / String.toUpperCase(Locale)')
w('// classes, see migration/backlog/T023.md. Under InvariantGlobalization there is no culture data, so the facts the Kotlin code takes from')
w('// java.util.Locale are kept here as data. Do not edit by hand; regenerate with the harness described in the T023 notes.')
w('namespace Unciv.Models.Translations;')
w('')
w('/// <summary>Per-language <c>DecimalFormat</c> facts as reported by <c>NumberFormat.getInstance(LocaleCode.getLocale(language))</c>.</summary>')
w('public sealed record JavaNumberSymbols(')
for l in ['string LocaleTag,','string LocaleLanguage,','char ZeroDigit,','char GroupingSeparator,','char DecimalSeparator,','int GroupingSize,','string NegativePrefix,','string NaN);']: w('    '+l)
w('')
w('internal static class JavaLocaleData')
w('{')
w('    /// <summary>All entries share: grouping used, max 3 / min 0 fraction digits, min 1 integer digit, HALF_EVEN, empty positive affixes and negative suffix, infinity U+221E.</summary>')
w('    internal static readonly IReadOnlyDictionary<string, JavaNumberSymbols> NumberSymbols = new Dictionary<string, JavaNumberSymbols>(StringComparer.Ordinal)')
w('    {')
for r in rows:
    name,tag,ll,zero,grp,dec,gs,used,np,ns,pp,ps,maxf,minf,mini,rnd,always,mult,nan,inf=r
    assert (used,hx(ns),hx(pp),hx(ps),maxf,minf,mini,rnd,always,mult,inf)==('true','','','','3','0','1','HALF_EVEN','false','1','221e'),r
    w('        ["%s"] = new("%s", "%s", %s, %s, %s, %d, "%s", "%s"),'%(esc(name),tag,ll,chesc(chr(int(zero))),chesc(chr(int(grp))),chesc(chr(int(dec))),int(gs),esc(hx(np)),esc(hx(nan))))
w('    };')
w('')
w('    /// <summary>The case mapping of the JVM (<c>String.valueOf(c).toUpperCase(locale)</c> / <c>toLowerCase</c>) wherever it differs from <c>char.ToUpperInvariant/ToLowerInvariant</c>. Key "" is <c>Locale.ROOT</c>, used for every language without its own entry.</summary>')
w('    internal static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<char, (string Upper, string Lower)>> CaseMappings =')
w('        new Dictionary<string, IReadOnlyDictionary<char, (string Upper, string Lower)>>(StringComparer.Ordinal)')
w('        {')
locs=sorted(set(r[2] for r in rows))
for loc in ['']+[l for l in ['lt','tr','az'] if l in locs]:
    w('            ["%s"] = new Dictionary<char, (string Upper, string Lower)>'%loc)
    w('            {')
    cnt=0
    for c in sorted(set(per[loc])|set(net)):
        j=per[loc].get(c,(chr(c),chr(c))); n=net.get(c,(chr(c),chr(c)))
        if j!=n:
            w('                [%s] = ("%s", "%s"),'%(chesc(chr(c)),esc(j[0]),esc(j[1]))); cnt+=1
    w('            },')
    print(loc,cnt)
w('        };')
w('}')
open('../../../Unciv.Core/Models/Translations/JavaLocaleData.cs','w',encoding='utf-8').write('\n'.join(out)+'\n')
# which locale languages have distinct case from root?
rootset=per['']
for loc in locs:
    if loc and per[loc]!=rootset: print('distinct',loc)

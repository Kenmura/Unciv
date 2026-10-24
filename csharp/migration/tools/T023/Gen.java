import com.unciv.models.metadata.LocaleCode;
import java.text.*;
import java.util.*;
public class Gen { public static void main(String[] a) throws Exception {
  java.io.PrintStream num=new java.io.PrintStream("num.tsv","UTF-8"), cs=new java.io.PrintStream("case.tsv","UTF-8");
  List<String> langs=new ArrayList<>(); for (LocaleCode lc: LocaleCode.getEntries()) langs.add(lc.languageName());
  // keep LocaleCode enum order (not sorted): that is the order of getSupportedLanguages
  TreeSet<String> localeLangs=new TreeSet<>();
  for (String lang: langs){ Locale l=LocaleCode.Companion.getLocale(lang); localeLangs.add(l.getLanguage()); DecimalFormat f=(DecimalFormat)NumberFormat.getInstance(l); DecimalFormatSymbols s=f.getDecimalFormatSymbols();
    num.println(String.join("\t", lang, l.toLanguageTag(), l.getLanguage(), ""+(int)s.getZeroDigit(), ""+(int)s.getGroupingSeparator(), ""+(int)s.getDecimalSeparator(), ""+f.getGroupingSize(), ""+f.isGroupingUsed(), hex(f.getNegativePrefix()), hex(f.getNegativeSuffix()), hex(f.getPositivePrefix()), hex(f.getPositiveSuffix()), ""+f.getMaximumFractionDigits(), ""+f.getMinimumFractionDigits(), ""+f.getMinimumIntegerDigits(), ""+f.getRoundingMode(), ""+f.isDecimalSeparatorAlwaysShown(), ""+f.getMultiplier(), hex(s.getNaN()), hex(s.getInfinity())));
  }
  List<Locale> locs=new ArrayList<>(); locs.add(Locale.ROOT); for (String ll: localeLangs) locs.add(Locale.forLanguageTag(ll));
  for (Locale loc: locs) for (int c=0;c<0x10000;c++){ if (c>=0xD800&&c<=0xDFFF) continue; String s=String.valueOf((char)c); String u=s.toUpperCase(loc), lo=s.toLowerCase(loc);
    if (!u.equals(s)||!lo.equals(s)) cs.println(loc.getLanguage()+"\t"+Integer.toHexString(c)+"\t"+hex(u)+"\t"+hex(lo)); }
}
 static String hex(String s){ StringBuilder b=new StringBuilder(); for (char c: s.toCharArray()) { if (b.length()>0) b.append(','); b.append(Integer.toHexString(c)); } return b.toString(); }
}

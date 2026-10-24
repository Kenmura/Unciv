import com.unciv.models.metadata.LocaleCode;
import java.text.*;
import java.util.*;
public class Nm2 { 
 static String hexs(String s){ StringBuilder b=new StringBuilder(); for (char c: s.toCharArray()) { if (b.length()>0) b.append(','); b.append(Integer.toHexString(c)); } return b.toString(); }
 public static void main(String[] a) throws Exception {
  java.io.PrintStream out=new java.io.PrintStream("nf.tsv","UTF-8");
  Number[] nums={0,1,-1,999,1000,1234,-1234,1234567,-1234567,0.5,0.0625,0.0005,1234.5678,12.3456789,2.5f,0.1f,1.0E10,Long.MAX_VALUE,Long.MIN_VALUE,-0.0,-0.0004,0.0004,0.0005001,Double.NaN,Double.POSITIVE_INFINITY,Double.NEGATIVE_INFINITY,1.0E25,123456789012345678901234567890.0};
  // every language, a fixed set
  for (LocaleCode lc: LocaleCode.getEntries()) { String lang=lc.languageName(); Locale l=LocaleCode.Companion.getLocale(lang); DecimalFormat f=(DecimalFormat)NumberFormat.getInstance(l);
    for (Number n: nums) { String kind = n instanceof Long||n instanceof Integer ? "L" : (n instanceof Float ? "S" : "D");
      String val = kind.equals("L") ? ""+n.longValue() : (kind.equals("S") ? Integer.toHexString(Float.floatToRawIntBits(n.floatValue())) : Long.toHexString(Double.doubleToRawLongBits(n.doubleValue())));
      out.println("N\t"+lang+"\t"+kind+"\t"+val+"\t"+hexs(((DecimalFormat)f.clone()).format(n))); }
    // reformat probes
    String[] probes={"0","1","12","1234","1,234","1.234","1 234","1 234","1 234","-5","-0","007","12345678901234567890","99999999999999999999999","9223372036854775807","9223372036854775808","-9223372036854775808","12,34","1,2345","x","","-","1,","১২৩৪","١٢٣","۱۲","１２"};
    for (String p: probes) { DecimalFormat g=(DecimalFormat)f.clone(); ParsePosition pos=new ParsePosition(0); Number n=g.parse(p,pos); String r = n==null?"null":(pos.getIndex()==p.length()? hexs(g.format(n)) : "partial"); out.println("P\t"+lang+"\t"+hexs(p)+"\t"+r); }
    // symbol-formatted probes: format some numbers, then reformat
    for (long v: new long[]{1234, -1234, 1234567, 99999, -7}) { DecimalFormat g=(DecimalFormat)f.clone(); String p=g.format(v); ParsePosition pos=new ParsePosition(0); Number n=g.parse(p,pos); String r = n==null?"null":(pos.getIndex()==p.length()? hexs(g.format(n)) : "partial"); out.println("P\t"+lang+"\t"+hexs(p)+"\t"+r); }
  }
  // fuzz: 5 languages
  String[] fl={"English","Afrikaans","Arabic","Bangla","Toki_Pona","German"}; Random r=new Random(20260101);
  for (String lang: fl) { DecimalFormat f=(DecimalFormat)NumberFormat.getInstance(LocaleCode.Companion.getLocale(lang));
    List<Double> ds=new ArrayList<>();
    for (int i=-4000;i<=4000;i++){ ds.add(i/1000.0); ds.add(i/8000.0); ds.add(i/16000.0+0.0000625); ds.add(i/200.0); }
    for (int i=0;i<1500;i++){ ds.add((r.nextDouble()-0.5)*Math.pow(10,r.nextInt(14)-6)); ds.add((double)(r.nextFloat()-0.5f)*(float)Math.pow(10,r.nextInt(8)-4)); ds.add(r.nextInt(2000000)/1000.0 - 1000); ds.add(r.nextLong()/1e3); }
    for (double d: ds) out.println("D\t"+lang+"\t"+Long.toHexString(Double.doubleToRawLongBits(d))+"\t"+hexs(((DecimalFormat)f.clone()).format(d)));
    for (int i=0;i<1000;i++){ long v=r.nextLong()>>(r.nextInt(60)); out.println("L\t"+lang+"\t"+v+"\t"+hexs(((DecimalFormat)f.clone()).format(v))); }
  }
  out.close(); }}

using System;
using System.Collections.Generic;
using System.Linq;
using BmwDeepObd;

int count = 0;
void Check(bool ok, string name)
{
    if (!ok) throw new Exception("FAIL: " + name);
    count++;
    Console.WriteLine("PASS: " + name);
}
Dictionary<string,string> Row(string dimension, string unit, string value) =>
    new() { ["ST_UN_BOS_TEXT"]=dimension, ["RMMI_BOS_EINH"]=unit, ["RMMI_BOS_WERT"]=value };
string Value(Dictionary<string,string> row) => BavarianCbsPresentation.Value(row,false);
Check(Value(Row("Weg","x 10 km","-1506"))=="Overdue per ECU: 15,060 km","CBS negative scaled distance");
Check(Value(Row("Weg","km","9000"))=="Remaining per ECU: 9,000 km","CBS unscaled distance");
Check(Value(Row("Weg","km","-32768"))=="Interval unavailable","CBS sentinel");
Check(Value(Row("Weg","unknown","9000"))=="Interval unavailable","CBS unknown unit");
Check(Value(Row("Zeit","Tage","12"))=="ECU days: 12","CBS days");
var date=Row("Zeit","Tage","-32768");
date["ZIEL_MM_WERT"]="2"; date["ZIEL_YY_WERT"]="27";
Check(Value(date)=="ECU target month/year: 02/27","CBS two digit year");
date["ZIEL_MM_WERT"]="13";
Check(Value(date)=="Interval unavailable","CBS invalid month");
date["ZIEL_MM_WERT"]="2"; date["ZIEL_YY_WERT"]="2027";
Check(Value(date)=="Target month: 02/2027","CBS full year");
Check(BavarianCbsPresentation.Label("Motoroel",true)=="Моторное масло","CBS Russian label");
var groups=BavarianScanCatalog.DiscoveryGroups;
Check(groups.Length==24 && groups.Distinct().Count()==24,"24 unique group names");
Check(BavarianScanCatalog.AllGroups.Length==10 && BavarianScanCatalog.AllGroups.All(groups.Contains),"baseline subset");
Check(groups.All(g=>!string.IsNullOrWhiteSpace(BavarianScanCatalog.Module(g))),"module labels");
Check(groups.Where(BavarianScanCatalog.IsExtended).All(g=>BavarianScanCatalog.TryGroupHash(g,out var h)&&h.Length==64&&h.All(Uri.IsHexDigit)),"extended group hashes");
Check(!BavarianScanCatalog.TryVariantHash("d_ars.grp","unknown.prg",out _),"unknown variant rejected");
Console.WriteLine($"{count} checks passed.");

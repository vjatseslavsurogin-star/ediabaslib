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
var order=BavarianVehicleOrder.ParseText("E60#0905*NC71%475&LCSW$205$522$609");
Check(order!=null&&order.Options.SequenceEqual(new[]{"205","522","609"}),"strict textual FA sample");
Check(BavarianVehicleOrder.ParseText("FF00AABB$205")==null,"opaque FA rejected");
Check(BavarianVehicleOrder.ParseText("E60#1905*NC71%475&LCSW$205")==null,"invalid FA month rejected");
Check(BavarianVehicleOrder.ParseText("E60#0905*NC71%475&LCSW$205$205")==null,"duplicate FA option rejected");
const string vin="WBANC71000A123456";
Check(BavarianScanSnapshot.ValidVin(vin)&&!BavarianScanSnapshot.ValidVin("INVALID"),"VIN shape");
BavarianScanSnapshot.ScanGroup G(string module,string variant,bool read,params string[] codes)=>new(){Module=module,Variant=variant,Read=read,Codes=codes};
var a=new BavarianScanSnapshot{Vin=vin,Time="A",Groups=new[]{G("DDE","d50m57e0.prg",true,"4501","4548"),G("EGS","gs19a.prg",true,"5150")}};
var b=new BavarianScanSnapshot{Vin=vin,Time="B",Groups=new[]{G("DDE","d50m57e0.prg",true,"4501","4521"),G("EGS",null,false)}};
var report=string.Join("\n",BavarianScanSnapshot.Compare(a,b,false));
Check(report.Contains("New records: 4521")&&report.Contains("Still recorded: 4501")&&report.Contains("Not seen in later recognized records: 4548"),"compare recognized records");
Check(report.Contains("EGS · No comparable later response")&&!report.Contains("5150"),"failed group not called cleared");
b.Vin="WBANC71000A123457";
bool rejected=false;try{BavarianScanSnapshot.Compare(a,b,false);}catch(InvalidOperationException){rejected=true;}
Check(rejected,"different VIN rejected");
Console.WriteLine($"{count} checks passed.");

#if BAVARIAN_USB_BASELINE
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Android.Hardware.Usb;
using EdiabasLib;
namespace BmwDeepObd
{
    public partial class BavarianVehicleActivity
    {
        private string _maintenanceMode,_maintenanceIdentity,_maintenanceDevice;
        private long _maintenanceReadAt;
        private Button _maintenanceRead,_maintenanceWrite,_maintenanceStop;
        private static bool IsMaintenanceMode(string mode)=>mode=="battery"||mode=="battery_read"||mode=="cbs"||mode=="ima"||mode=="mileage"||mode=="glow"||mode=="ecu_info";
        private void CreateMaintenance(string mode)
        {
            _maintenanceMode=mode;SupportActionBar?.Hide();
            BavarianUi.Shell(this,mode=="battery"?3:1,_ru,i=>{if(_busy){_output.Text=T("Stop the request first.","Сначала остановите запрос.");return;}BavarianUi.NavigateHome(this,i);},out _body);
            AddText(mode switch {"battery" or "battery_read"=>T("Battery management","Аккумулятор"),"cbs"=>T("CBS service","Сервис CBS"),"ima"=>T("Injector codes","Коды форсунок"),"mileage"=>T("Mileage check","Проверка пробега"),"glow"=>T("Glow system","Свечи накаливания"),_=>T("ECU information","Информация ECU")},26);
            AddText(mode=="battery_read"?T("DDE 5.0 · IBS · read-only battery diagnostics.","DDE 5.0 · IBS · диагностика АКБ без записи."):mode=="battery"?T("DDE 5.0 · IBS · replacement history. Registration only for a newly installed battery of unchanged type and capacity.","DDE 5.0 · IBS · история замен. Регистрация только нового АКБ прежнего типа и ёмкости."):T("Read-only · approved ECU variants. Park safely, ignition on.","Только чтение · подтверждённые варианты ECU. Стоянка, зажигание включено."),14);
            _state=AddText("",14);
            _maintenanceRead=AddButton(T("Read data","Прочитать данные"),async(s,e)=>await MaintenanceAsync(false));BavarianUi.Style(_maintenanceRead,true);
            if(mode=="battery")
            {
                _maintenanceWrite=AddButton(T("Register new battery","Зарегистрировать новый АКБ"),(s,e)=>ConfirmBattery());
                AddText(T("Registration does not change battery coding. Current and AGM type are not available in this profile. SOH is the ECU's raw estimate, not a capacity test.","Регистрация не меняет кодировку АКБ. Ток и тип AGM недоступны в этом профиле. SOH — оценка ECU, не измерение фактической ёмкости."),13);
            }
            if(mode=="mileage")AddText(T("Mileage records may be synchronized or stored with offsets. Matching values do not prove authenticity.","Пробеги могут синхронизироваться или храниться со смещением. Совпадение не доказывает подлинность."),13);
            _maintenanceStop=AddButton(T("Stop request","Остановить запрос"),(s,e)=>_cancel?.Cancel());
            _output=AddText(T("No readings yet.","Данные ещё не прочитаны."),15);
            AddButton(T("Share developer report","Поделиться инженерным отчётом"),(s,e)=>ShareReport());
            _poll=new Java.Lang.Runnable(()=>{if(!_visible)return;RefreshMaintenance();_handler.PostDelayed(_poll,1000);});
            RefreshMaintenance();
        }
        private void RefreshMaintenance()
        {
            if(_state==null||IsFinishing||IsDestroyed)return;
            bool ready=false,usb=false;
            try
            {
                var m=Manager;var d=GetDevice(m);ready=Ready(m,d);usb=ready&&!d.Bluetooth;
                if(!ready||d.DeviceName!=_maintenanceDevice){_maintenanceIdentity=null;_maintenanceReadAt=0;}
                if(_busy&&(!ready||d.DeviceName!=_activeDevice))_cancel?.Cancel();
            }
            catch(Exception){_maintenanceIdentity=null;_maintenanceReadAt=0;_cancel?.Cancel();}
            _state.Text=_busy?T("● Reading / request running","● Выполняется запрос"):ready?usb?T("● Adapter connected","● Адаптер подключён"):T("● Bluetooth selected · experimental","● Bluetooth выбран · тест"):T("○ Not connected · Home","○ Нет подключения · Главная");
            _state.SetTextColor(ready&&usb?BavarianUi.Green:BavarianUi.Muted);
            _maintenanceRead.Enabled=ready&&!_busy;_maintenanceStop.Visibility=_busy?ViewStates.Visible:ViewStates.Gone;
            if(_maintenanceWrite!=null)_maintenanceWrite.Enabled=usb&&!_busy&&_maintenanceIdentity!=null&&SystemClock.ElapsedRealtime()-_maintenanceReadAt<120000;
        }
        private void ConfirmBattery()
        {
            if(_busy||_maintenanceWrite==null||!_maintenanceWrite.Enabled)return;
            new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(T("Register battery replacement?","Зарегистрировать замену АКБ?"))
                .SetMessage(T("Experimental DDE 5.0 procedure. Confirm a NEW battery of the SAME coded capacity and type is physically installed. Vehicle parked, ignition on, engine stopped, stable approved power supply. This resets battery replacement history/learning. Do not register an old battery or repeat an uncertain result.","Экспериментальная процедура DDE 5.0. Подтвердите, что установлен НОВЫЙ АКБ ТОЙ ЖЕ закодированной ёмкости и типа. Машина стоит, зажигание включено, двигатель заглушён, стабильный источник питания. Операция регистрирует замену и обновляет данные энергоменеджмента. Не регистрируйте старый АКБ и не повторяйте операцию при неопределённом результате."))
                .SetNegativeButton(T("Cancel","Отмена"),(d,a)=>{})
                .SetPositiveButton(T("Confirm replacement","Подтвердить замену"),async(d,a)=>await MaintenanceAsync(true)).Show();
        }
        private async Task MaintenanceAsync(bool write)
        {
            if(_busy)return;
            UsbManager m;BavarianEndpoint d;
            try{m=Manager;d=GetDevice(m);if(!Ready(m,d))return;}
            catch(Exception ex){_output.Text=Describe(ex);return;}
            string identity=_maintenanceIdentity;
            if(write&&(_maintenanceMode!="battery"||d.Bluetooth||identity==null||d.DeviceName!=_maintenanceDevice||SystemClock.ElapsedRealtime()-_maintenanceReadAt>=120000))return;
            _maintenanceIdentity=null;_maintenanceReadAt=0;_busy=true;_activeDevice=d.DeviceName;_cancel=new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var token=_cancel.Token;RefreshMaintenance();_output.Text=T("Waiting for ECU…","Ожидание ECU…");
            try
            {
                var result=await Task.Run(()=>ReadMaintenance(m,d.DeviceName,_maintenanceMode,write,identity,token));
                if(IsFinishing||IsDestroyed||!_visible)return;
                _output.Text=result.Text;
                if(!write&&result.Identity!=null&&!token.IsCancellationRequested)
                {_maintenanceIdentity=result.Identity;_maintenanceDevice=d.DeviceName;_maintenanceReadAt=SystemClock.ElapsedRealtime();}
                if(!SaveHistory("Maintenance "+_maintenanceMode,result.Text,token.IsCancellationRequested||result.Incomplete))
                    _output.Text+="\n"+T("Local report not saved.","Локальный отчёт не сохранён.");
            }
            catch(Exception ex)
            {
                Log("MAINTENANCE FAIL "+Describe(ex));
                if(!IsFinishing&&!IsDestroyed)_output.Text=(write?T("Registration not verified. Do not repeat automatically. Read battery history first.\n","Регистрация не подтверждена. Не повторяйте автоматически. Сначала прочитайте историю АКБ.\n"):"")+Describe(ex);
            }
            finally{_cancel.Dispose();_cancel=null;_busy=false;_activeDevice=null;if(!IsFinishing&&!IsDestroyed)RefreshMaintenance();}
        }
        private sealed class MaintenanceResult{public string Text,Identity;public bool Incomplete;}
        private MaintenanceResult ReadMaintenance(UsbManager manager,string endpoint,string mode,bool write,string expected,CancellationToken token)
        {
            if(!TransportLock.Wait(0))throw new InvalidOperationException("Transport busy");
            var result=new MaintenanceResult();var lines=new List<string>();
            try
            {
                if(write&&mode!="battery")throw new InvalidOperationException("Write not allowed in read-only mode");
                var targets=mode=="mileage"||mode=="cbs"?new[]{"d_motor.grp","d_kombi.grp"}:mode=="ecu_info"?new[]{"d_motor.grp","d_egs.grp","d_kombi.grp","d_cas.grp"}:new[]{"d_motor.grp"};
                foreach(var group in targets)
                {
                    token.ThrowIfCancellationRequested();
                    var current=GetDevice(manager);
                    if(!Ready(manager,current)||current.DeviceName!=endpoint||(write&&current.Bluetooth))throw new IOException("Adapter changed");
                    try
                    {
                        if(!Groups.TryGetValue(group,out var hash)&&!BavarianScanCatalog.TryGroupHash(group,out hash))throw new IOException("Group not approved");
                        VerifyFile(group,hash);VerifyFile("t_grtb.prg","7b564434408d5256606d26b0f57b50739b7e029ef3cef967748a08eb05717791");
                        using var ed=new EdiabasNet();ed.SetConfigProperty("EcuPath",Root);ed.AbortJobFunc=()=>token.IsCancellationRequested;
                        var transport=new EdInterfaceObd();ed.EdInterfaceClass=transport;BavarianConnection.Configure(this,manager,current,transport);
                        using var reg=token.Register(()=>transport.TransmitCancel(true));
                        ed.ResolveSgbdFile(group);token.ThrowIfCancellationRequested();
                        string variant=ed.SgbdFileName?.ToLowerInvariant()??"";
                        if(!VariantMatches(group,variant))throw new IOException("Unapproved variant: "+variant);
                        VerifyFile(variant,BavarianScanCatalog.IsExtended(group)&&BavarianScanCatalog.TryVariantHash(group,variant,out var vh)?vh:Approved[variant]);
                        lines.Add(BavarianScanCatalog.Module(group)+" · "+variant);
                        List<Dictionary<string,string>> Read(string job,bool requireStatus=true)
                        {
                            token.ThrowIfCancellationRequested();ed.ArgString=string.Empty;ed.ArgBinaryStd=null;ed.ResultsRequests=string.Empty;
                            ed.ExecuteJob(job);token.ThrowIfCancellationRequested();
                            var sets=ed.ResultSets;if(sets==null||sets.Count<2)throw new IOException("No ECU results: "+job);
                            var rows=sets.Skip(1).Select(r=>r.ToDictionary(k=>k.Key,k=>k.Value.OpData is byte[] bytes?Convert.ToHexString(bytes):Convert.ToString(k.Value.OpData,CultureInfo.InvariantCulture))).ToList();
                            var statuses=rows.Where(r=>r.ContainsKey("JOB_STATUS")).Select(r=>r["JOB_STATUS"]).ToArray();
                            if((requireStatus&&statuses.Length==0)||statuses.Any(s=>!string.Equals(s,"OKAY",StringComparison.OrdinalIgnoreCase)))throw new IOException("Unsuccessful "+job);
                            Log("MAINTENANCE "+variant+" "+job+"\n"+string.Join("\n",rows.SelectMany(r=>r.Select(k=>k.Key+": "+k.Value))));
                            return rows;
                        }
                        var ident=Read("IDENT",false);
                        var idfields=ident.SelectMany(r=>r.Where(k=>k.Key!="JOB_STATUS")).Select(k=>k.Key+": "+k.Value).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
                        if(idfields.Length==0)throw new IOException("No identification data");
                        var id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",idfields))));
                        if(mode=="ecu_info"){lines.AddRange(idfields.Where(x=>!x.StartsWith("_")));continue;}
                        if(variant!="d50m57e0.prg"&&variant!="komb60.prg")throw new IOException("Service profile unavailable for "+variant);
                        string Field(List<Dictionary<string,string>> rows,string key)
                        {
                            var values=rows.Where(r=>r.ContainsKey(key)).Select(r=>r[key]).Distinct(StringComparer.Ordinal).ToArray();return values.Length==1?values[0]:null;
                        }
                        string Format(List<Dictionary<string,string>> rows,string key)
                        {
                            var val=Field(rows,key+"_WERT");var unit=Field(rows,key+"_EINH");
                            return string.IsNullOrEmpty(val)?"—":val+" "+(unit??"");
                        }
                        double Number(List<Dictionary<string,string>> rows,string key,string unit)
                        {
                            if(Field(rows,key+"_EINH")!=unit||!double.TryParse(Field(rows,key+"_WERT"),NumberStyles.Float,CultureInfo.InvariantCulture,out var number)||!double.IsFinite(number))throw new IOException("Unverified field "+key);
                            return number;
                        }
                        if(mode=="battery"||mode=="battery_read")
                        {
                            if(variant!="d50m57e0.prg")throw new IOException("DDE 5.0 required");
                            var ibs=Read("IDENT_IBS");var pm1=Read("STATUS_SYSTEMCHECK_PM_INFO_1");var pm2=Read("STATUS_SYSTEMCHECK_PM_INFO_2");var voltage=Read("STATUS_UBATT");
                            lines.Add(T("IBS part: ","Номер IBS: ")+(Field(ibs,"ID_BMW_NR")??"—"));
                            lines.Add(T("Capacity: ","Ёмкость: ")+Format(pm2,"STAT_BATTERIE_KAPAZITAET"));
                            lines.Add("SOC: "+Format(pm2,"STAT_LADUNGSZUSTAND_AKTUELL"));
                            lines.Add(T("SOH (raw ECU estimate): ","SOH (оценка ECU): ")+Format(pm2,"STAT_SOH"));
                            lines.Add(T("DDE voltage: ","Напряжение DDE: ")+Format(voltage,"STAT_UBATT"));
                            lines.Add(T("Seasonal temperature: ","Сезонная температура: ")+Format(pm2,"STAT_TEMP_SAISON"));
                            lines.Add(T("Charge balance: ","Баланс заряда: ")+Format(pm1,"STAT_BATTERIELADUNG_BILANZ"));
                            var history=new[]{"LETZTER","ZWEITLETZTER","DRITTLETZTER","VIERTLETZTER","FUENFTLETZTER"};
                            lines.Add(T("Replacement history (ECU records):","История замен (записи ECU):"));
                            foreach(var item in history)lines.Add("  "+Format(pm1,"STAT_BATTERIETAUSCH_"+item));
                            foreach(var row in pm2)foreach(var kv in row.Where(k=>k.Key.StartsWith("STAT_IBS_FEHLERZAEHLER")&&k.Key.EndsWith("_WERT")))lines.Add(kv.Key+": "+kv.Value);
                            if(!write)
                            {
                                if(mode=="battery"&&!current.Bluetooth&&Number(pm2,"STAT_BATTERIE_KAPAZITAET","Ah")>0&&Number(voltage,"STAT_UBATT","V")>=12.0&&Number(voltage,"STAT_UBATT","V")<=15.0)result.Identity=id;
                            }
                            else
                            {
                                if(id!=expected)throw new IOException("DDE identity changed");
                                var rpm=Read("STATUS_MOTORDREHZAHL");if(Number(rpm,"STAT_MOTORDREHZAHL","rpm")!=0)throw new IOException("Engine must be stopped");
                                var freshVoltage=Read("STATUS_UBATT");var volts=Number(freshVoltage,"STAT_UBATT","V");if(volts<12.0||volts>15.0)throw new IOException("Voltage outside conservative registration gate");
                                if(Number(pm2,"STAT_BATTERIE_KAPAZITAET","Ah")<=0)throw new IOException("Capacity unavailable");
                                var before=Field(pm1,"STAT_BATTERIETAUSCH_LETZTER_WERT");if(before==null)throw new IOException("Replacement history missing");
                                if(!SaveHistory("Battery pre-registration",string.Join("\n",lines),false))throw new IOException("Cannot save pre-registration report");
                                token.ThrowIfCancellationRequested();var now=GetDevice(manager);if(!Ready(manager,now)||now.Bluetooth||now.DeviceName!=endpoint)throw new IOException("Adapter changed");
                                Read("STEUERN_BATTERIETAUSCH_REGISTRIEREN");
                                var after=Read("STATUS_SYSTEMCHECK_PM_INFO_1");
                                var newest=Field(after,"STAT_BATTERIETAUSCH_LETZTER_WERT");
                                lines.Add(T("Registration acknowledged by ECU. History reread: ","ECU подтвердил команду. История перечитана: ")+Format(after,"STAT_BATTERIETAUSCH_LETZTER"));
                                if(newest==null||before==newest){lines.Add(T("History change not independently verified. Do not repeat automatically.","Изменение истории независимо не подтверждено. Не повторяйте автоматически."));result.Incomplete=true;}
                            }
                        }
                        else if(mode=="mileage")
                        {
                            if(variant=="d50m57e0.prg")lines.Add("DDE: "+Format(Read("STATUS_KILOMETERSTAND"),"STAT_KILOMETERSTAND"));
                            else {var km=Read("STATUS_GLOBAL_KM");string unit=Field(km,"STAT_GWSZ_EINH")??"";foreach(var key in new[]{"RAM","EEPROM","CAS"})lines.Add("KOMBI "+key+": "+(Field(km,"STAT_GWSZ_"+key)??"—")+" "+unit);}
                        }
                        else if(mode=="cbs")
                        {
                            var data=Read("CBS_DATEN_LESEN");
                            foreach(var row in data)
                            {
                                if(!row.TryGetValue("ID_FN_BOS_MESS_TEXT",out var label))continue;
                                if(label.IndexOf("CSF",StringComparison.OrdinalIgnoreCase)>=0||label.IndexOf("Partikel",StringComparison.OrdinalIgnoreCase)>=0)continue;
                                lines.Add(label+" · "+string.Join(" · ",row.Where(k=>k.Key=="RMMI_BOS_WERT"||k.Key=="RMMI_BOS_EINH"||k.Key=="ST_UN_BOS_TEXT"||k.Key=="COU_RSTG_BOS_MESS_WERT"||k.Key=="AVAI_BOS_WERT"||k.Key=="ZIEL_MM_WERT"||k.Key=="ZIEL_YY_WERT").Select(k=>k.Key+": "+k.Value)));
                            }
                            if(!data.Any(r=>r.ContainsKey("ID_FN_BOS_MESS_TEXT")))lines.Add(T("No recognized service entries.","Нет распознанных записей CBS."));
                        }
                        else if(mode=="ima")
                        {
                            var data=Read("ABGLEICH_IMA_LESEN");
                            for(int i=1;i<=6;i++)lines.Add(T("Cylinder ","Цилиндр ")+i+": "+(Field(data,"ABGLEICH_IMA_WERT_ZYL"+i)??"—"));
                            lines.Add(T("Code length: ","Длина кода: ")+(Field(data,"ANZAHL_ZEICHEN_IMA_WERTE")??"—"));
                        }
                        else if(mode=="glow")
                        {
                            var data=Read("FS_LESEN",false);
                            var records=BavarianFaultPanel.ParseRecords(data);
                            var found=records.Where(r=>r.Values.Any(x=>x!=null&&(x.IndexOf("glüh",StringComparison.OrdinalIgnoreCase)>=0||x.IndexOf("glueh",StringComparison.OrdinalIgnoreCase)>=0||x.IndexOf("glow",StringComparison.OrdinalIgnoreCase)>=0))).ToArray();
                            lines.Add(T("Matching fault records: ","Подходящие записи ошибок: ")+found.Length);
                            foreach(var record in found)lines.AddRange(record.Where(k=>!k.Key.StartsWith("_")).Select(k=>k.Key+": "+k.Value));
                            lines.Add(T("No matching records does not prove glow system health.","Отсутствие записей не доказывает исправность свечей."));
                        }
                    }
                    catch(Exception ex){if(write||token.IsCancellationRequested)throw;result.Incomplete=true;lines.Add(T("Read incomplete: ","Чтение неполное: ")+Describe(ex));}
                    lines.Add("");
                }
                result.Text=string.Join("\n",lines);return result;
            }
            finally{TransportLock.Release();}
        }
    }
}
#endif

#if BAVARIAN_USB_BASELINE
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using AndroidX.AppCompat.App;
namespace BmwDeepObd
{
    [Activity(Label="Backup & restore",Exported=false,Name=ActivityCommon.AppNameSpace+"."+nameof(BavarianBackupActivity))]
    public class BavarianBackupActivity:AppCompatActivity
    {
        private const int Limit=32*1024*1024;
        private const int RecordLimit=512*1024;
        private static readonly string[] BoolKeys={"compact","fullscreen","haptics","animations","metrics"};
        private static readonly string[] TextKeys={"appearance","favorites"};
        private static readonly Regex HistoryName=new Regex(@"\A[0-9]{8}-[0-9]{6}-[0-9]{3}-[0-9a-f]{8}\.(?:txt|scan\.json)\z",RegexOptions.CultureInvariant);
        private bool _ru;
        private TextView _state;
        private string T(string en,string ru)=>_ru?ru:en;
        private ISharedPreferences Pref(string name)=>GetSharedPreferences(name,FileCreationMode.Private);
        private string History=>Path.Combine(FilesDir.AbsolutePath,"bavarian-history");
        private sealed class Backup
        {
            public Backup(){}
            public int Schema {get;set;}=1;
            public Dictionary<string,bool> Flags {get;set;}=new();
            public Dictionary<string,string> Settings {get;set;}=new();
            public Dictionary<string,string> Dashboards {get;set;}=new();
            public string Language {get;set;}
            public string Vin {get;set;}
            public Dictionary<string,string> Profile {get;set;}=new();
            public Dictionary<string,string> History {get;set;}=new();
        }
        protected override void OnCreate(Bundle saved)
        {
            BavarianUi.ApplyTheme(this);base.OnCreate(saved);SupportActionBar?.Hide();_ru=BavarianLanguage.IsRussian(this);
            BavarianUi.Shell(this,4,_ru,i=>BavarianUi.NavigateHome(this,i),out var body);
            body.AddView(BavarianUi.Text(this,T("Backup & restore","Резервная копия"),26,true));
            body.AddView(BavarianUi.Text(this,T("Local file: preferences, dashboard selections, latest VIN profile and diagnostic history. Does not include adapter credentials, ECU library or signing keys. May contain VIN and fault data.","Локальный файл: настройки, параметры панели, последний профиль VIN и история диагностики. Не содержит учётных данных адаптера, базы ECU и ключей подписи. Может содержать VIN и ошибки."),14));
            BavarianUi.Action(body,T("Export backup","Экспортировать копию"),(s,e)=>Pick(false),true);
            BavarianUi.Action(body,T("Restore from backup","Восстановить из копии"),(s,e)=>Pick(true));
            _state=BavarianUi.Text(this,"",14);body.AddView(_state);
        }
        private void Pick(bool restore)
        {
            var intent=new Intent(restore?Intent.ActionOpenDocument:Intent.ActionCreateDocument);
            intent.AddCategory(Intent.CategoryOpenable);intent.SetType("application/json");
            if(!restore)intent.PutExtra(Intent.ExtraTitle,"bavarian-backup.json");
            StartActivityForResult(intent,restore?731:730);
        }
        private IEnumerable<string> ProfileKeys()
        {
            foreach(var key in new[]{"time","notes","raw_fa","fa_time"})yield return key;
            foreach(var g in BavarianScanCatalog.DiscoveryGroups)foreach(var p in new[]{"observed_","detail_","owner_","responded_at_"})yield return p+g;
        }
        private Backup Capture()
        {
            var b=new Backup();var home=Pref("bavarian_home");
            foreach(var k in BoolKeys)if(home.Contains(k))b.Flags[k]=home.GetBoolean(k,false);
            foreach(var k in TextKeys)if(home.Contains(k))b.Settings[k]=home.GetString(k,"");
            var dash=Pref("bavarian_dashboard");
            foreach(var g in new[]{"d_motor.grp","d_egs.grp"})if(dash.Contains(g))b.Dashboards[g]=dash.GetString(g,"");
            b.Language=BavarianLanguage.IsRussian(this)?"ru":"en";
            string vin=Pref("bavarian_overview").GetString("vin","");
            if(BavarianScanSnapshot.ValidVin(vin))
            {
                var profile=Pref("bavarian_discovery_"+vin);
                if(profile.GetBoolean("saved",false))
                {
                    b.Vin=vin;foreach(var k in ProfileKeys())if(profile.Contains(k))b.Profile[k]=profile.GetString(k,"");
                }
            }
            if(Directory.Exists(History))
            {
                foreach(var file in Directory.GetFiles(History).Where(p=>HistoryName.IsMatch(Path.GetFileName(p))).OrderByDescending(p=>p,StringComparer.Ordinal).Take(100))
                {
                    if(new FileInfo(file).Length>RecordLimit)throw new IOException("Report exceeds backup size limit");
                    b.History[Path.GetFileName(file)]=File.ReadAllText(file);
                }
            }
            return b;
        }
        private static void Validate(Backup b)
        {
            if(b==null||b.Schema!=1||b.Flags==null||b.Settings==null||b.Dashboards==null||b.Profile==null||b.History==null||b.History.Count>100)throw new IOException("Invalid backup schema");
            if(b.Vin!=null&&!BavarianScanSnapshot.ValidVin(b.Vin))throw new IOException("Invalid backup VIN");
            if(b.Profile.Count>150||b.Settings.Count>16||b.Flags.Count>16||b.Dashboards.Count>4||b.Profile.Any(p=>p.Value==null||p.Value.Length>16384)||b.Settings.Any(p=>p.Value==null||p.Value.Length>4096)||b.Dashboards.Any(p=>p.Value==null||p.Value.Length>4096))throw new IOException("Invalid settings length");
            foreach(var p in b.History)
            {
                if(!HistoryName.IsMatch(p.Key)||p.Value==null||Encoding.UTF8.GetByteCount(p.Value)>RecordLimit)throw new IOException("Invalid backup history entry");
                if(p.Key.EndsWith(".scan.json",StringComparison.Ordinal))
                {
                    var x=JsonSerializer.Deserialize<BavarianScanSnapshot>(p.Value);
                    if(x==null||x.Schema!=1||x.Groups==null||x.Groups.Length>24||x.Groups.Any(g=>g==null||string.IsNullOrEmpty(g.Module)||g.Codes==null||g.Codes.Length>4096||g.Codes.Any(c=>c==null||c.Length>16)||(g.States!=null&&(g.States.Count>4096||g.States.Any(k=>k.Key.Length>16||k.Value==null||k.Value.Length>32))))||x.Groups.Select(g=>g.Module).Distinct().Count()!=x.Groups.Length)throw new IOException("Invalid scan snapshot");
                }
            }
        }
        private void Restore(Backup b)
        {
            Validate(b);
            var edit=Pref("bavarian_home").Edit();
            foreach(var k in BoolKeys)if(b.Flags.TryGetValue(k,out bool value))edit.PutBoolean(k,value);
            if(b.Settings.TryGetValue("appearance",out var theme)&&new[]{"system","dark","light"}.Contains(theme))edit.PutString("appearance",theme);
            if(b.Settings.TryGetValue("favorites",out var fav))edit.PutString("favorites",fav);
            edit.Apply();
            var dash=Pref("bavarian_dashboard").Edit();foreach(var g in new[]{"d_motor.grp","d_egs.grp"})if(b.Dashboards.TryGetValue(g,out var signals))dash.PutString(g,signals);dash.Apply();
            if(b.Language=="en"||b.Language=="ru")BavarianLanguage.SetRussian(this,b.Language=="ru");
            bool restoredProfile=false;
            if(BavarianScanSnapshot.ValidVin(b.Vin)&&b.Profile.Count>0)
            {
                var p=Pref("bavarian_discovery_"+b.Vin);
                if(!p.GetBoolean("saved",false))
                {
                    var e=p.Edit();foreach(var k in ProfileKeys())if(b.Profile.TryGetValue(k,out var v))e.PutString(k,v);
                    // An imported profile never satisfies Smart Scan's current complete-discovery gate.
                    e.PutBoolean("saved",true).PutBoolean("complete",false).Apply();restoredProfile=true;
                    if(string.IsNullOrEmpty(Pref("bavarian_overview").GetString("vin","")))Pref("bavarian_overview").Edit().PutString("vin",b.Vin).Apply();
                }
            }
            Directory.CreateDirectory(History);
            int added=0;
            foreach(var p in b.History.Where(x=>x.Key.EndsWith(".txt",StringComparison.Ordinal)))
            {
                var target=Path.Combine(History,p.Key);if(File.Exists(target))continue;
                File.WriteAllText(target,p.Value,Encoding.UTF8);added++;
                var companion=Path.ChangeExtension(p.Key,".scan.json");
                if(b.History.TryGetValue(companion,out var json)&&!File.Exists(Path.Combine(History,companion)))
                {
                    var snap=JsonSerializer.Deserialize<BavarianScanSnapshot>(json);snap.Imported=true;
                    File.WriteAllText(Path.Combine(History,companion),JsonSerializer.Serialize(snap),Encoding.UTF8);
                }
            }
            _state.Text=T("Settings restored. New reports: ","Настройки восстановлены. Новых отчётов: ")+added+"\n"+(restoredProfile?T("Historical profile restored; run discovery to confirm.","Исторический профиль восстановлен; выполните поиск для подтверждения."):T("No profile replaced.","Существующий профиль не заменён."))+"\n"+T("Existing reports kept. Reopen the app to apply language and appearance changes.","Существующие отчёты сохранены. Перезапустите приложение для применения языка и оформления.");
        }
        protected override void OnActivityResult(int requestCode,Result resultCode,Intent data)
        {
            base.OnActivityResult(requestCode,resultCode,data);
            if(resultCode!=Result.Ok||data?.Data==null||(requestCode!=730&&requestCode!=731))return;
            try
            {
                if(requestCode==730)
                {
                    var bytes=JsonSerializer.SerializeToUtf8Bytes(Capture());if(bytes.Length>Limit)throw new IOException("Backup too large");
                    using var output=ContentResolver.OpenOutputStream(data.Data,"wt");if(output==null)throw new IOException("Cannot open destination");output.Write(bytes,0,bytes.Length);
                    _state.Text=T("Backup exported.","Копия экспортирована.");
                }
                else
                {
                    using var input=ContentResolver.OpenInputStream(data.Data);if(input==null)throw new IOException("Cannot open backup");
                    using var memory=new MemoryStream();var buffer=new byte[8192];int n;
                    while((n=input.Read(buffer,0,buffer.Length))>0){if(memory.Length+n>Limit)throw new IOException("Backup too large");memory.Write(buffer,0,n);}
                    var b=JsonSerializer.Deserialize<Backup>(memory.ToArray());Validate(b);
                    new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(T("Restore backup?","Восстановить копию?"))
                        .SetMessage(T("Replace supported settings and add missing history. Existing reports and profiles are not overwritten. Imported profile needs new discovery.","Заменить поддерживаемые настройки и добавить отсутствующую историю. Существующие отчёты и профили не заменяются. Импортированный профиль требует нового поиска."))
                        .SetNegativeButton(T("Cancel","Отмена"),(d,a)=>{}).SetPositiveButton(T("Restore","Восстановить"),(d,a)=>{try{Restore(b);}catch(Exception ex){_state.Text=T("Restore incomplete: ","Восстановление не завершено: ")+ex.GetType().Name;}}).Show();
                }
            }
            catch(Exception ex){_state.Text=T("Backup operation failed: ","Ошибка резервной копии: ")+ex.GetType().Name;}
        }
    }
}
#endif

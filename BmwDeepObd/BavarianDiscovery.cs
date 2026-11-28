#if BAVARIAN_USB_BASELINE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.Hardware.Usb;
using Android.Views;
using Android.Widget;
namespace BmwDeepObd
{
    public partial class BavarianVehicleActivity
    {
        private bool _discovery,_smartScan;
        private string _discoveryVin,_scanProfileNote="";
        private LinearLayout _discoveryCards;
        private Button _discoveryStart,_discoveryStop;
        private TextView _discoverySummary;
        private readonly Dictionary<string,(string State,string Detail)> _discoveryResults=new();
        private ISharedPreferences DiscoveryProfile(string vin)=>GetSharedPreferences("bavarian_discovery_"+vin,FileCreationMode.Private);
        private void CreateDiscovery()
        {
            _discovery=true;SupportActionBar?.Hide();
            BavarianUi.Shell(this,1,_ru,i=>{if(_busy){_output.Text=T("Stop discovery first.","Сначала остановите поиск.");return;}BavarianUi.NavigateHome(this,i);},out _body);
            AddText(T("Vehicle Discovery","Поиск оборудования"),26);
            AddText(T("Read-only identification of 24 candidate groups. Groups can overlap; a failed response does not prove absence. Park safely, ignition on.","Идентификация 24 групп-кандидатов без записи. Группы могут пересекаться; отсутствие ответа не доказывает отсутствие блока. Стоянка, зажигание включено."),14);
            _state=AddText("",14);
            _discoveryStart=AddButton(T("Start discovery","Начать поиск"),async(s,e)=>await DiscoverAsync());BavarianUi.Style(_discoveryStart,true);
            _discoveryStop=AddButton(T("Stop discovery","Остановить поиск"),(s,e)=>_cancel?.Cancel());
            _output=AddText(T("No current vehicle identified.","Текущий автомобиль ещё не определён."),14);
            _discoverySummary=AddText("",15);
            _discoveryCards=new LinearLayout(this){Orientation=Orientation.Vertical};_body.AddView(_discoveryCards);
            _poll=new Java.Lang.Runnable(()=>{if(!_visible)return;RefreshDiscovery();_handler.PostDelayed(_poll,1000);});
            RefreshDiscovery();
        }
        private void RefreshDiscovery()
        {
            if(_state==null||IsFinishing||IsDestroyed)return;
            bool ready=false;
            try{var m=Manager;var d=GetDevice(m);ready=Ready(m,d);if(_busy&&(!ready||d.DeviceName!=_activeDevice))_cancel?.Cancel();}catch(Exception){_cancel?.Cancel();}
            _state.Text=_busy?T("Identification running","Выполняется идентификация"):ready?T("Adapter available","Адаптер доступен"):T("Adapter not connected","Адаптер не подключён");
            _state.SetTextColor(_busy?BavarianUi.Blue:ready?BavarianUi.Warning:BavarianUi.Muted);
            _discoveryStart.Enabled=ready&&!_busy;_discoveryStop.Visibility=_busy?ViewStates.Visible:ViewStates.Gone;
        }
        private async Task DiscoverAsync()
        {
            if(_busy)return;
            UsbManager manager;BavarianEndpoint device;
            try{manager=Manager;device=GetDevice(manager);if(!Ready(manager,device))return;}catch(Exception ex){_output.Text=Describe(ex);return;}
            _discoveryVin=null;_discoveryResults.Clear();_discoveryCards.RemoveAllViews();_discoverySummary.Text="";
            _busy=true;_activeDevice=device.DeviceName;_cancel=new CancellationTokenSource(TimeSpan.FromMinutes(8));var token=_cancel.Token;
            RefreshDiscovery();
            string fa=null;
            try
            {
                foreach(var group in BavarianScanCatalog.DiscoveryGroups)
                {
                    if(token.IsCancellationRequested||!_visible)break;
                    _output.Text=T("Identifying ","Идентификация ")+BavarianScanCatalog.Module(group);
                    using var step=CancellationTokenSource.CreateLinkedTokenSource(token);step.CancelAfter(TimeSpan.FromSeconds(group=="d_cas.grp"?25:15));
                    try
                    {
                        var r=await Task.Run(()=>Execute(manager,device.DeviceName,group,new[]{"IDENT"},null,null,step.Token,false,null,null,false,group=="d_cas.grp",group=="d_cas.grp"));
                        if(token.IsCancellationRequested||!_visible)break;
                        if(string.IsNullOrEmpty(r.Identity))throw new System.IO.IOException("No verified identification");
                        _discoveryResults[group]=("responded",r.Variant);
                        if(group=="d_cas.grp"){_discoveryVin=r.Vin;fa=r.VehicleOrder;}
                        RememberEcuReply(BavarianScanCatalog.Module(group)+" · "+r.Variant);
                    }
                    catch(Exception ex)
                    {
                        if(token.IsCancellationRequested||!_visible)break;
                        string message=Describe(ex);
                        string status=message.Contains("Detected variant is not approved:")?"unsupported":"unconfirmed";
                        _discoveryResults[group]=(status,step.IsCancellationRequested?T("Timed out","Тайм-аут"):message);
                    }
                    _discoverySummary.Text=_discoveryResults.Count+" / "+BavarianScanCatalog.DiscoveryGroups.Length+" · "+T("groups checked","групп проверено");
                }
                if(!_visible||IsFinishing||IsDestroyed)return;
                bool complete=!token.IsCancellationRequested&&_discoveryResults.Count==BavarianScanCatalog.DiscoveryGroups.Length;
                if(!string.IsNullOrEmpty(_discoveryVin))
                {
                    var profile=DiscoveryProfile(_discoveryVin);
                    var edit=profile.Edit().PutBoolean("saved",true).PutBoolean("complete",complete).PutString("time",DateTime.Now.ToString("dd.MM.yyyy HH:mm",CultureInfo.InvariantCulture));
                    foreach(var group in BavarianScanCatalog.DiscoveryGroups)
                    {
                        if(_discoveryResults.TryGetValue(group,out var r))edit.PutString("observed_"+group,r.State).PutString("detail_"+group,r.Detail);
                        else edit.PutString("observed_"+group,"unconfirmed").PutString("detail_"+group,"Not checked");
                    }
                    if(fa!=null)edit.PutString("raw_fa",fa);
                    else edit.Remove("raw_fa");
                    edit.Apply();
                    GetSharedPreferences("bavarian_overview",FileCreationMode.Private).Edit().PutString("vin",_discoveryVin).Apply();
                }
                _output.Text=(complete?T("Discovery finished.","Поиск завершён."):T("Partial discovery.","Поиск выполнен частично."))+"\n"+(string.IsNullOrEmpty(_discoveryVin)?T("VIN not confirmed; profile not saved.","VIN не подтверждён; профиль не сохранён."):T("VIN profile saved: ","Профиль сохранён для VIN: ")+_discoveryVin)+"\n"+(fa!=null?T("FA read; raw data only, options not decoded.","FA прочитан; исходные данные, опции не расшифрованы."):T("FA not available.","FA недоступен."));
            }
            finally
            {
                _cancel.Dispose();_cancel=null;_busy=false;_activeDevice=null;
                if(_visible&&!IsFinishing&&!IsDestroyed){RenderDiscovery();RefreshDiscovery();}
            }
        }
        private void RenderDiscovery()
        {
            _discoveryCards.RemoveAllViews();
            var profile=string.IsNullOrEmpty(_discoveryVin)?null:DiscoveryProfile(_discoveryVin);
            int responding=_discoveryResults.Count(x=>x.Value.State=="responded");
            _discoverySummary.Text=responding+" / "+BavarianScanCatalog.DiscoveryGroups.Length+" · "+T("groups responded (not unique ECUs)","групп ответили (не число отдельных ECU)");
            foreach(var group in BavarianScanCatalog.DiscoveryGroups)
            {
                var r=_discoveryResults.TryGetValue(group,out var found)?found:("unconfirmed",T("Not checked","Не проверен"));
                string owner=profile?.GetString("owner_"+group,"")??"";
                bool yes=r.Item1=="responded",unsupported=r.Item1=="unsupported";
                string status=yes?T("Responded","Ответил"):unsupported?T("Variant not supported","Вариант не поддерживается"):owner=="absent"?T("Owner: not installed; not confirmed by scan","По данным владельца отсутствует; поиском не подтверждено"):owner=="expected"?T("Expected; response not confirmed","Ожидается; ответ не подтверждён"):T("Presence unconfirmed","Наличие не подтверждено");
                if(yes&&owner=="absent")status+=T(" · conflicts with owner note"," · противоречит отметке владельца");
                var card=new LinearLayout(this){Orientation=Orientation.Vertical};card.Background=BavarianUi.Shape(this,BavarianUi.Surface);card.SetPadding(Dp(12),Dp(10),Dp(12),Dp(10));
                card.AddView(BavarianUi.Text(this,BavarianScanCatalog.Module(group),18,true));
                var state=BavarianUi.Text(this,status,13,true);state.SetTextColor(yes?BavarianUi.Green:unsupported||owner=="expected"?BavarianUi.Warning:BavarianUi.Muted);card.AddView(state);
                card.AddView(BavarianUi.Text(this,r.Item2,12));
                if(profile!=null)
                {
                    string label=owner=="absent"?T("Owner: not installed","Владелец: отсутствует"):owner=="expected"?T("Owner: expected","Владелец: установлен") :T("Owner: unknown","Владелец: неизвестно");
                    BavarianUi.Action(card,label,(s,e)=>{if(_busy)return;new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(BavarianScanCatalog.Module(group)).SetItems(new[]{T("Unknown","Неизвестно"),T("Installed / expected","Установлен / ожидается"),T("Not installed / removed","Не установлен / демонтирован")},(a,b)=>{profile.Edit().PutString("owner_"+group,new[]{"","expected","absent"}[b.Which]).Apply();RenderDiscovery();}).Show();});
                }
                _discoveryCards.AddView(card,new LinearLayout.LayoutParams(-1,-2){TopMargin=Dp(8)});
            }
            if(profile!=null)
            {
                var raw=profile.GetString("raw_fa","");
                if(!string.IsNullOrEmpty(raw))BavarianUi.Action(_discoveryCards,T("Raw vehicle order · FA","Исходный заказ автомобиля · FA"),(s,e)=>new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle("FA / VO · raw").SetMessage(raw).SetPositiveButton("OK",(a,b)=>{}).Show());
                var note=new EditText(this){Hint=T("Aftermarket equipment / notes","Нештатное оборудование / заметки"),Text=profile.GetString("notes","")};_discoveryCards.AddView(note);
                BavarianUi.Action(_discoveryCards,T("Save vehicle notes","Сохранить заметки"),(s,e)=>{profile.Edit().PutString("notes",note.Text??"").Apply();Toast.MakeText(this,T("Saved","Сохранено"),ToastLength.Short).Show();});
            }
        }
    }
}
#endif

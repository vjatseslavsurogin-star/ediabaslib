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
        private bool _showUnconfirmed;
        private readonly Dictionary<string,(string State,string Detail)> _discoveryResults=new();
        private readonly Dictionary<string,string> _discoveryIdentities=new();
        private bool _historicalDiscovery;
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
            var savedVin=GetSharedPreferences("bavarian_overview",FileCreationMode.Private).GetString("vin","");
            if(!string.IsNullOrEmpty(savedVin)&&DiscoveryProfile(savedVin).GetBoolean("saved",false))
            {
                _discoveryVin=savedVin;_historicalDiscovery=true;var saved=DiscoveryProfile(savedVin);
                foreach(var group in BavarianScanCatalog.DiscoveryGroups)
                    _discoveryResults[group]=(saved.GetString("observed_"+group,"unconfirmed"),saved.GetString("detail_"+group,""));
                _output.Text=T("Saved profile, not verified in this session: ","Сохранённый профиль, не подтверждён в этой сессии: ")+savedVin+" · "+saved.GetString("time","");
                RenderDiscovery();
            }
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
            _discoveryVin=null;_historicalDiscovery=false;_discoveryIdentities.Clear();_discoveryResults.Clear();_discoveryCards.RemoveAllViews();_discoverySummary.Text="";
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
                        _discoveryResults[group]=("responded",r.Variant);_discoveryIdentities[group]=r.Identity;
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
            _discoverySummary.Text=(_historicalDiscovery?T("Saved: ","Сохранено: "):"")+responding+" / "+BavarianScanCatalog.DiscoveryGroups.Length+" · "+T("groups responded (not unique ECUs)","групп ответили (не число отдельных ECU)");
            _discoveryCards.AddView(BavarianUi.Text(this,T("ECU overview · logical groups, not wiring","Карта ECU · логические группы, не схема шин"),16,true));
            var map=new LinearLayout(this){Orientation=Orientation.Vertical};_discoveryCards.AddView(map);
            LinearLayout mapRow=null;int index=0;
            foreach(var group in BavarianScanCatalog.DiscoveryGroups)
            {
                var state=_discoveryResults.TryGetValue(group,out var observed)?observed.State:"unconfirmed";
                var owner=profile?.GetString("owner_"+group,"")??"";
                if(index++%3==0){mapRow=new LinearLayout(this){Orientation=Orientation.Horizontal};map.AddView(mapRow);}
                var tile=BavarianUi.Text(this,BavarianScanCatalog.Module(group),12,true);tile.Gravity = GravityFlags.Center;
                tile.SetPadding(Dp(4),Dp(8),Dp(4),Dp(8));
                tile.Background=BavarianUi.Shape(this,BavarianUi.Surface,8);
                tile.SetTextColor(state=="responded"?BavarianUi.Green:state=="unsupported"||owner=="expected"?BavarianUi.Warning:BavarianUi.Muted);
                tile.Clickable=true;tile.Focusable=true;tile.ContentDescription=BavarianScanCatalog.Module(group)+" · "+state;
                tile.Click+=(s,e)=>{if(_busy)return;var label=BavarianScanCatalog.Module(group);
                    var detail=_discoveryResults.TryGetValue(group,out var info)?info.Detail:T("Not checked","Не проверен");
                    new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(label).SetMessage((_historicalDiscovery?T("Historical response","Исторический ответ"):T("Current discovery","Текущий поиск"))+"\n"+detail)
                        .SetPositiveButton(T("Read faults","Читать ошибки"),(d,a)=>{if(_busy)return;var intent=new Intent(this,typeof(BavarianVehicleActivity));intent.PutExtra("bavarian_mode","selected_scan");intent.PutExtra("bavarian_groups",new[]{group});StartActivity(intent);})
                        .SetNegativeButton(T("Close","Закрыть"),(d,a)=>{}).Show();
                };
                mapRow.AddView(tile,new LinearLayout.LayoutParams(0,Dp(60),1){RightMargin=Dp(3),BottomMargin=Dp(3)});
            }
            _discoveryCards.AddView(BavarianUi.Text(this,T("Green: responded · amber: expected/unsupported · gray: not confirmed","Зелёный: ответил · янтарный: ожидается/не поддерживается · серый: не подтверждён"),12));
            if(_discoveryResults.TryGetValue("d_ccc.grp",out var ccc)&&_discoveryResults.TryGetValue("d_mmi.grp",out var mmi)&&ccc.State=="responded"&&mmi.State=="responded"&&ccc.Detail=="ccc_60.prg"&&mmi.Detail=="ccc_60.prg")
            {
                bool same=_discoveryIdentities.TryGetValue("d_ccc.grp",out var cccId)&&_discoveryIdentities.TryGetValue("d_mmi.grp",out var mmiId)&&cccId==mmiId;
                _discoveryCards.AddView(BavarianUi.Text(this,same?T("CCC / MMI: same variant and IDENT fingerprint; possible overlapping routes, not verified physical identity.","CCC / MMI: совпадают вариант и IDENT; возможное пересечение, физический блок не подтверждён."):T("CCC / MMI: same variant; possible overlap. Count as groups only.","CCC / MMI: одинаковый вариант; возможное пересечение. Считаются только группы."),13));
            }
            var filter=new CheckBox(this){Text=T("Show all candidates (including unknown / absent)","Показать все группы (неизвестные / отсутствующие)"),Checked=_showUnconfirmed};BavarianUi.Checkbox(filter);
            filter.CheckedChange+=(a,b)=>{_showUnconfirmed=b.IsChecked;RenderDiscovery();};_discoveryCards.AddView(filter);
            foreach(var group in BavarianScanCatalog.DiscoveryGroups)
            {
                var r=_discoveryResults.TryGetValue(group,out var found)?found:("unconfirmed",T("Not checked","Не проверен"));
                string owner=profile?.GetString("owner_"+group,"")??"";
                bool yes=r.Item1=="responded",unsupported=r.Item1=="unsupported";
                if(!_showUnconfirmed&&!yes&&!unsupported&&owner!="expected"&&owner!="retrofit")continue;
                string status=yes?T("Responded","Ответил"):unsupported?T("Variant not supported","Вариант не поддерживается"):owner=="absent"?T("Owner: not installed; not confirmed by scan","По данным владельца отсутствует; поиском не подтверждено"):owner=="expected"?T("Expected; response not confirmed","Ожидается; ответ не подтверждён"):T("Presence unconfirmed","Наличие не подтверждено");
                if(yes&&owner=="absent")status+=T(" · conflicts with owner note"," · противоречит отметке владельца");
                var card=new LinearLayout(this){Orientation=Orientation.Vertical};card.Background=BavarianUi.Shape(this,BavarianUi.Surface);card.SetPadding(Dp(12),Dp(10),Dp(12),Dp(10));
                card.AddView(BavarianUi.Text(this,BavarianScanCatalog.Module(group),18,true));
                var state=BavarianUi.Text(this,status,13,true);state.SetTextColor(yes?BavarianUi.Green:unsupported||owner=="expected"?BavarianUi.Warning:BavarianUi.Muted);card.AddView(state);
                card.AddView(BavarianUi.Text(this,r.Item2,12));
                if(profile!=null)
                {
                    string label=owner=="absent"?T("Owner: not installed","Владелец: отсутствует"):owner=="expected"?T("Owner: expected","Владелец: установлен"):owner=="retrofit"?T("Owner: retrofitted","Владелец: дооснащение"):T("Owner: unknown","Владелец: неизвестно");
                    BavarianUi.Action(card,label,(s,e)=>{if(_busy)return;new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(BavarianScanCatalog.Module(group)).SetItems(new[]{T("Unknown","Неизвестно"),T("Installed / expected","Установлен / ожидается"),T("Not installed / removed","Не установлен / демонтирован"),T("Retrofitted / aftermarket","Дооснащение / нештатный") },(a,b)=>{profile.Edit().PutString("owner_"+group,new[]{"","expected","absent","retrofit"}[b.Which]).Apply();RenderDiscovery();}).Show();});
                }
                _discoveryCards.AddView(card,new LinearLayout.LayoutParams(-1,-2){TopMargin=Dp(8)});
            }
            if(profile!=null)
            {
                var raw=profile.GetString("raw_fa","");
                if(!string.IsNullOrEmpty(raw))
                {
                    _discoveryCards.AddView(BavarianUi.Text(this,T("Factory vehicle order · FA","Заводская комплектация · FA"),18,true));
                    var order=BavarianVehicleOrder.ParseText(raw);
                    if(order==null)_discoveryCards.AddView(BavarianUi.Text(this,T("FA read; binary / unknown format. Factory options not decoded.","FA считан; двоичный / неизвестный формат. Опции не расшифрованы."),13));
                    else
                    {
                        _discoveryCards.AddView(BavarianUi.Text(this,order.Series+" · "+order.Date+" · "+order.Type,15,true));
                        _discoveryCards.AddView(BavarianUi.Text(this,T("Paint / trim: ","Краска / салон: ")+order.Paint+" / "+order.Trim,13));
                        _discoveryCards.AddView(BavarianUi.Text(this,T("Explicit SA codes: ","Явные SA-коды: ")+(order.Options.Length==0?"—":string.Join(", ",order.Options)),14));
                        _discoveryCards.AddView(BavarianUi.Text(this,T("Text FA only. Factory options do not prove present-day equipment.","Только текстовый FA. Заводские опции не подтверждают фактическое оборудование."),12));
                    }
                }if(!string.IsNullOrEmpty(raw))BavarianUi.Action(_discoveryCards,T("Raw vehicle order · FA","Исходный заказ автомобиля · FA"),(s,e)=>new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle("FA / VO · raw").SetMessage(raw).SetPositiveButton("OK",(a,b)=>{}).Show());
                var note=new EditText(this){Hint=T("Aftermarket equipment / notes","Нештатное оборудование / заметки"),Text=profile.GetString("notes","")};_discoveryCards.AddView(note);
                BavarianUi.Action(_discoveryCards,T("Save vehicle notes","Сохранить заметки"),(s,e)=>{profile.Edit().PutString("notes",note.Text??"").Apply();Toast.MakeText(this,T("Saved","Сохранено"),ToastLength.Short).Show();});
            }
        }
    }
}
#endif

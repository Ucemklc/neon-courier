using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Optional skill bonus; passing the stop still delivers cargo normally.</summary>
public sealed class CourierTimedDelivery : MonoBehaviour
{
    public static CourierTimedDelivery Instance { get; private set; }
    private GameManager manager;
    private RunnerController runner;
    private Transform parcel;
    private Vector3 from,to;
    private float flight,noticeUntil,clock;
    public DeliveryZone Target { get; private set; }
    public float SecondsToStop { get; private set; }
    public int LastResult { get; private set; }
    public bool HasNotice => clock < noticeUntil;
    public bool CanThrow => CourierJourney.Active && Target && !Target.ThrowAttempted && manager.CarriedPackages>0 && SecondsToStop>=.18f && SecondsToStop<=2.1f;
    public bool PerfectWindow => SecondsToStop>=.55f && SecondsToStop<=.95f;
    private void Awake(){Instance=this;manager=GetComponent<GameManager>();}
    private void Start()
    {
        runner=FindAnyObjectByType<RunnerController>();
        var prefab=Resources.Load<GameObject>("CourierJourney/ThrowParcel");
        if(prefab){parcel=Instantiate(prefab).transform;parcel.gameObject.SetActive(false);}
        manager.MainMenuChanged+=Menu;
    }
    private void OnDestroy(){if(manager)manager.MainMenuChanged-=Menu;if(parcel)Destroy(parcel.gameObject);if(Instance==this)Instance=null;}
    private void Menu(bool open){if(!open)return;Target=null;noticeUntil=0;flight=0;if(parcel)parcel.gameObject.SetActive(false);}
    private void Update()
    {
        if(!CourierJourney.Active)return;
        clock+=Time.deltaTime;RefreshTarget();
        if(Keyboard.current!=null&&Keyboard.current.fKey.wasPressedThisFrame)TryThrow();
        if(parcel&&parcel.gameObject.activeSelf)
        {
            flight+=Time.deltaTime;float t=Mathf.Clamp01(flight/.55f);
            parcel.position=Vector3.Lerp(from,to,t)+Vector3.up*(Mathf.Sin(t*Mathf.PI)*2.2f);
            parcel.rotation=Quaternion.Euler(t*300,t*180,15);
            if(t>=1)parcel.gameObject.SetActive(false);
        }
    }
    public void RefreshTarget()
    {
        Target=null;SecondsToStop=float.PositiveInfinity;
        if(!runner||!CourierJourney.Active||manager.CarriedPackages<=0)return;
        float speed=Mathf.Max(1,runner.CurrentForwardSpeed*(CourierCargoManifest.Instance?CourierCargoManifest.Instance.SpeedMultiplier:1));
        foreach(var zone in DeliveryZone.ActiveStops)
        {
            if(!zone||zone.IsDelivered||zone.ThrowAttempted)continue;
            float seconds=(zone.transform.position.z-runner.transform.position.z)/speed;
            if(seconds>=.18f&&seconds<=2.1f&&seconds<SecondsToStop){Target=zone;SecondsToStop=seconds;}
        }
    }
    public bool TryThrow()
    {
        RefreshTarget();if(!CanThrow)return false;
        var zone=Target;zone.ThrowAttempted=true;
        bool accurate=SecondsToStop>=.25f&&SecondsToStop<=1.5f;
        bool perfect=PerfectWindow;
        int delivered=accurate?zone.TryDeliver():0;
        LastResult=delivered>0?(perfect?2:1):0;noticeUntil=clock+2.5f;
        if(delivered>0){manager.GrantRunBonus(perfect?8:3);CourierCareerProgress.AddXP(perfect?10:5);}
        if(parcel)
        {
            from=runner.transform.position+new Vector3(.25f,.65f,.4f);
            to=zone.DestinationPosition+(delivered>0?Vector3.zero:new Vector3(-1,-.7f,-3));
            flight=0;parcel.position=from;parcel.gameObject.SetActive(true);
        }
        Target=null;return true;
    }
    public string Notice(bool en)=>LastResult==2?(en?"PERFECT DELIVERY  +8 COINS":"TAM İSABET!  +8 COIN"):LastResult==1?(en?"GOOD THROW  +3 COINS":"GÜZEL ATIŞ!  +3 COIN"):(en?"TOO EARLY · Deliver at the stop":"ERKEN ATIŞ · Durakta teslim edebilirsin");
}

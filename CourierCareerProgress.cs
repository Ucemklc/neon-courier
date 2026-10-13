using System;
using UnityEngine;

/// <summary>Permanent delivery progression, independent of the existing wallet and character unlocks.</summary>
public sealed class CourierCareerProgress : MonoBehaviour
{
    public const string XPKey="NeonCourier.CareerXP";
    public const string DeliveriesKey="NeonCourier.CareerDeliveries";
    public const string ContractsKey="NeonCourier.CareerContracts";
    public const string RoofsKey="NeonCourier.CareerRoofs";
    public const string EscapesKey="NeonCourier.CareerEscapes";
    public const string OutfitKey="NeonCourier.CareerOutfit";
    private static readonly int[] Thresholds={0,150,400,800,1400,2200,3200};
    public static readonly string[] SavedKeys={XPKey,DeliveriesKey,ContractsKey,RoofsKey,EscapesKey,OutfitKey};
    public static event Action Changed;
    private GameManager manager;
    public static int XP=>Mathf.Max(0,PlayerPrefs.GetInt(XPKey,0));
    public static int Level{get{int level=1;for(int i=1;i<Thresholds.Length;i++)if(XP>=Thresholds[i])level=i+1;return level;}}
    public static int NextXP=>Level<Thresholds.Length?Thresholds[Level]:Thresholds[Thresholds.Length-1];
    public static float LevelProgress=>Level==Thresholds.Length?1:Mathf.InverseLerp(Thresholds[Level-1],NextXP,XP);
    public static int DistrictCount=>Level>=5?5:Level>=3?4:3;
    public static int Outfit=>Mathf.Clamp(PlayerPrefs.GetInt(OutfitKey,0),0,2);
    public static string Rank(bool en=false)=>Level>=6?(en?"CITY LEGEND":"ŞEHRİN EFSANESİ"):Level>=3?(en?"MASTER COURIER":"USTA KURYE"):(en?"ROOKIE COURIER":"ÇAYLAK KURYE");
    public static bool OutfitUnlocked(int index)=>index==0||index==1&&Level>=3||index==2&&Level>=6;
    public static bool PatchUnlocked(int index)=>index<3||index==3&&Level>=3||index==4&&Level>=6;
    public static bool SelectOutfit(int index){if(index<0||index>2||!OutfitUnlocked(index))return false;PlayerPrefs.SetInt(OutfitKey,index);PlayerPrefs.Save();Changed?.Invoke();return true;}
    private void Start(){manager=GetComponent<GameManager>();manager.DeliveryCompleted+=Delivery;}
    private void OnDestroy(){if(manager)manager.DeliveryCompleted-=Delivery;}
    private void Delivery(int packages){if(packages>0){Increment(DeliveriesKey,packages);AddXP(packages*10+15);}}
    public static void RecordContract(){Increment(ContractsKey,1);AddXP(60);}
    public static void RecordRoof(){Increment(RoofsKey,1);AddXP(40);}
    public static void RecordEscape(){Increment(EscapesKey,1);AddXP(35);}
    private static void Increment(string key,int count){PlayerPrefs.SetInt(key,Mathf.Min(1000000,PlayerPrefs.GetInt(key,0)+count));}
    public static void AddXP(int amount){if(amount<=0)return;PlayerPrefs.SetInt(XPKey,Mathf.Min(1000000,XP+amount));PlayerPrefs.Save();Changed?.Invoke();}
}

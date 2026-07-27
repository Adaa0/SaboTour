using UnityEngine;
using Mirror;

/// <summary>
/// SADECE ARABANIN SAHİBİ OLAN CLIENT'TA CINEMACHINE KAMERASINI AKTİF EDER
///
/// Senin mevcut Cinemachine kurulumun (Third Person Follow + Third Person
/// Aim) zaten çok iyi çalışıyordu — sorun kamera ayarları değildi, sorun
/// network'ün "hangi arabanın kamerası canlı olsun" bilgisini bilmemesiydi.
///
/// 4 araba spawn olunca, her birinin CarCam'ı (Cinemachine Camera) sahnede
/// duruyor. CinemachineBrain, aralarından "en yüksek Priority'li" olanı
/// otomatik seçiyor. Bunları network'e göre ayırmazsak, hepsi karışık bir
/// şekilde canlı kalmaya çalışıyor ya da yanlış araba takip ediliyor.
///
/// ÇÖZÜM: CarCam objesini PREFAB'DA BAŞLANGIÇTA KAPALI (inactive) bırak.
/// Bu script, SADECE arabanın sahibi olan client'ta (OnStartAuthority),
/// o arabanın CarCam'ını açıyor. Diğer 3 oyuncunun ekranında o araba için
/// CarCam hiç aktif olmuyor, hiç yarışmıyor.
///
/// KURULUM:
/// 1. Bu script'i Car (kök obje, NetworkIdentity'nin olduğu yer) üzerine ekle.
/// 2. Inspector'da "Car Cam" alanına, Hierarchy'deki CarCam objesini sürükle.
/// 3. CarCam objesini Hierarchy'de KAPALI (checkbox'ı kapalı) yap — prefab'ın
///    varsayılan hali böyle olsun.
/// 4. Cinemachine ayarlarına (Third Person Follow, Aim, Tracking Target vb.)
///    HİÇ DOKUNMA, aynen kalsın.
/// </summary>
public class CarCameraActivator : NetworkBehaviour
{
    [Tooltip("Bu arabanın CarCam objesi (Cinemachine Camera'nın bağlı olduğu obje). " +
             "Prefab'da BAŞLANGIÇTA KAPALI olmalı.")]
    [SerializeField] private GameObject carCam;

    /// <summary>
    /// Mirror callback — SADECE bu arabanın sahibi olan client'ta,
    /// yetki verildiği anda bir kere çağrılır.
    /// </summary>
    public override void OnStartAuthority()
    {
        base.OnStartAuthority();

        if (carCam != null)
        {
            carCam.SetActive(true);
        }
        else
        {
            Debug.LogWarning("[CarCameraActivator] carCam atanmamış! Inspector'dan CarCam objesini sürükle.");
        }
    }
}
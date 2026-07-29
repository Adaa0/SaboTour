using UnityEngine;
using Mirror;

/// <summary>
/// TAVUK SÜRÜSÜ SKİLİ — DriftTrap/IceBombSkill ile aynı desen. Asıl tavuk
/// spawn mantığı zaten ChickenFlockManager.cs'de hazırdı (network'ten önce
/// yazılmıştı), burada sadece server-authoritative seçim + her client'ı
/// aynı anda tetikleyen bir [ClientRpc] köprüsü var.
///
/// Tavuklar (Chicken.cs) networked DEĞİL — her client kendi kopyasını
/// yerel olarak oluşturuyor (IceBombSkill'deki gerekçenin aynısı: araba
/// çarpışma/yavaşlatma fiziği owner'ın kendi client'ında doğru çalışmalı).
/// Bu yüzden farklı client'larda tavukların TAM pozisyonu birebir aynı
/// olmayabilir (her biri kendi rastgele spawn noktalarını hesaplıyor) —
/// geçici test için kabul edilebilir, ileride gerekirse seed senkronizasyonu
/// eklenebilir (TrackSeedSync'teki gibi).
/// </summary>
public class ChickenFlockSkill : NetworkBehaviour
{
    private ChickenFlockManager flockManager;
    private int selectedCheckpointIndex = -1;

    void Start()
    {
        flockManager = FindAnyObjectByType<ChickenFlockManager>();
    }

    [Server]
    public void SelectCheckpoint(int index)
    {
        selectedCheckpointIndex = index;
    }

    [Server]
    public void ActivateSkill()
    {
        if (selectedCheckpointIndex < 0) return;
        RpcSpawnFlock(selectedCheckpointIndex);
    }

    [ClientRpc]
    private void RpcSpawnFlock(int index)
    {
        if (flockManager == null)
            flockManager = FindAnyObjectByType<ChickenFlockManager>();

        flockManager?.SpawnChickenFlockAtCheckpoint(index);
    }
}

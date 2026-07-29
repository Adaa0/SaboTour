using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;

/// <summary>
/// GEÇİCİ TEST GİRİŞİ — bkz. CLAUDE.md.
///
/// Sabotajcı şu an 3 skili de klavyeden test ediyor:
///   0-9 = checkpoint seç (üç skile de aynı anda bildirilir)
///   C   = Drift Trap'i aktive et
///   F   = Buz Bombası'nı aktive et
///   E   = Tavuk Sürüsü'nü aktive et
///
/// Bu SADECE network akışını (Command → Server → ClientRpc/TargetRpc) test
/// edebilmek için geçici bir çözüm — gerçek tasarımda checkpoint seçimi VE
/// skil seçimi kuledeki minimap/harita üzerinden mouse ile yapılacak (bkz.
/// CLAUDE.md madde 6 "Skill Seçimi"). İLERİDE bu script tamamen silinecek,
/// yerine UI butonlarının çağırdığı aynı Command'lar gelecek — DriftTrap /
/// IceBombSkill / ChickenFlockSkill tarafında hiçbir değişiklik gerekmeyecek.
/// </summary>
public class SaboteurSkillInput : NetworkBehaviour
{
    private static readonly Key[] DigitKeys =
    {
        Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
        Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9
    };

    void Update()
    {
        if (!isOwned) return;
        if (Keyboard.current == null) return;

        for (int i = 0; i < DigitKeys.Length; i++)
        {
            if (Keyboard.current[DigitKeys[i]].wasPressedThisFrame)
                CmdSelectCheckpoint(i);
        }

        if (Keyboard.current.cKey.wasPressedThisFrame)
            CmdActivateTrap();

        if (Keyboard.current.fKey.wasPressedThisFrame)
            CmdActivateIceBomb();

        if (Keyboard.current.eKey.wasPressedThisFrame)
            CmdActivateChickenFlock();
    }

    [Command]
    private void CmdSelectCheckpoint(int index)
    {
        FindAnyObjectByType<DriftTrap>()?.SelectCheckpoint(index);
        FindAnyObjectByType<IceBombSkill>()?.SelectCheckpoint(index);
        FindAnyObjectByType<ChickenFlockSkill>()?.SelectCheckpoint(index);

        TargetLog($"Checkpoint {index} seçildi.");
    }

    [Command]
    private void CmdActivateTrap()
    {
        DriftTrap driftTrap = FindAnyObjectByType<DriftTrap>();
        if (driftTrap == null) return;

        driftTrap.ActivateTrap();
        TargetLog("Drift Trap AKTİF!");
    }

    [Command]
    private void CmdActivateIceBomb()
    {
        IceBombSkill iceBomb = FindAnyObjectByType<IceBombSkill>();
        if (iceBomb == null) return;

        iceBomb.ActivateSkill();
        TargetLog("Buz Bombası gönderildi!");
    }

    [Command]
    private void CmdActivateChickenFlock()
    {
        ChickenFlockSkill flock = FindAnyObjectByType<ChickenFlockSkill>();
        if (flock == null) return;

        flock.ActivateSkill();
        TargetLog("Tavuk Sürüsü gönderildi!");
    }

    /// <summary>
    /// Server'dan SADECE bu sabotajcının sahibi olan client'a geri bildirim.
    /// Henüz gerçek bir kule UI'ı olmadığı için Console'a yazıyor — UI
    /// gelince buraya ekran metni güncellemesi eklenecek.
    /// </summary>
    [TargetRpc]
    private void TargetLog(string message)
    {
        Debug.Log($"[Sabotajcı] {message}");
    }
}

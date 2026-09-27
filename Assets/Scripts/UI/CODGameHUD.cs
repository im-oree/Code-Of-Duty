using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// In-match HUD for the LOCAL player. Fully authored inside the GameplayRig
/// prefab (canvas, plates and texts are scene objects — nothing is generated
/// at runtime); this component only fills the authored elements from the
/// local player's Invector components:
///   ammo   ← vShooterManager (clip / reserve, hidden with no gun)
///   health ← vHealthController (bar + number)
///   feed   ← CODNetworkHealth.PlayerKilled events
///   death  ← respawn overlay while the local player is dead
/// </summary>
public class CODGameHUD : MonoBehaviour
{
    [Header("Ammo (authored)")]
    public GameObject ammoPlate;
    public TextMeshProUGUI magazineText;
    public TextMeshProUGUI reserveText;
    public TextMeshProUGUI weaponNameText;

    [Header("Health (authored)")]
    public Image healthFill;
    public TextMeshProUGUI healthText;

    [Header("Kill feed (authored rows, newest first)")]
    public List<TextMeshProUGUI> killFeedRows = new List<TextMeshProUGUI>();
    public float killFeedRowLifetime = 6f;

    [Header("Death overlay (authored)")]
    public GameObject deathOverlay;

    readonly List<(string text, float time)> feed = new List<(string, float)>();

    Invector.vShooter.vShooterManager shooter;
    Invector.vHealthController health;
    GameObject boundPlayer;

    void OnEnable()
    {
        CODNetworkHealth.PlayerKilled += OnPlayerKilled;
    }

    void OnDisable()
    {
        CODNetworkHealth.PlayerKilled -= OnPlayerKilled;
    }

    void OnPlayerKilled(string attacker, string victim)
    {
        string entry = string.IsNullOrEmpty(attacker) ? victim + " DIED" : attacker + "  ➤  " + victim;
        feed.Insert(0, (entry, Time.time));
        if (feed.Count > killFeedRows.Count) feed.RemoveRange(killFeedRows.Count, feed.Count - killFeedRows.Count);
    }

    void Update()
    {
        BindLocalPlayer();
        UpdateAmmo();
        UpdateHealth();
        UpdateFeed();
    }

    void BindLocalPlayer()
    {
        var local = CODInvectorPlayer.Local;
        GameObject player = local != null ? local.gameObject : null;

        if (player == null)
        {
            // offline scene: any enabled COD input is the local player
            var input = FindFirstObjectByType<CODShooterInput>();
            if (input != null && input.enabled) player = input.gameObject;
        }

        if (player == boundPlayer) return;
        boundPlayer = player;
        shooter = player != null ? player.GetComponent<Invector.vShooter.vShooterManager>() : null;
        health = player != null ? player.GetComponent<Invector.vHealthController>() : null;
    }

    void UpdateAmmo()
    {
        var weapon = shooter != null ? shooter.rWeapon : null;
        bool show = weapon != null;

        if (ammoPlate != null && ammoPlate.activeSelf != show) ammoPlate.SetActive(show);
        if (!show) return;

        if (magazineText != null) magazineText.text = weapon.ammoCount.ToString();
        if (reserveText != null)
        {
            var ammo = shooter.ammoManager != null ? shooter.ammoManager.GetAmmo(weapon.ammoID) : null;
            reserveText.text = "/ " + (ammo != null ? ammo.count.ToString() : "0");
        }
        if (weaponNameText != null) weaponNameText.text = weapon.gameObject.name.ToUpperInvariant();
    }

    void UpdateHealth()
    {
        bool dead = health != null && health.isDead;
        if (deathOverlay != null && deathOverlay.activeSelf != dead) deathOverlay.SetActive(dead);

        if (health == null) return;
        float pct = health.maxHealth > 0 ? health.currentHealth / health.maxHealth : 0f;
        if (healthFill != null) healthFill.fillAmount = Mathf.Clamp01(pct);
        if (healthText != null) healthText.text = Mathf.CeilToInt(Mathf.Max(0f, health.currentHealth)).ToString();
    }

    void UpdateFeed()
    {
        feed.RemoveAll(f => Time.time - f.time > killFeedRowLifetime);
        for (int i = 0; i < killFeedRows.Count; i++)
        {
            if (killFeedRows[i] == null) continue;
            bool active = i < feed.Count;
            if (killFeedRows[i].gameObject.activeSelf != active) killFeedRows[i].gameObject.SetActive(active);
            if (active) killFeedRows[i].text = feed[i].text;
        }
    }
}

﻿using System.Collections.Generic;
using UnityEngine;

public class CustomTraderInteraction : MonoBehaviour, Interactable
{
    private List<Trader.TradeItem> tradeItems = new();

    public void SetupTraderItems(string itemsConfig)
    {
        string[] items = itemsConfig.Split(':');
        foreach (string itemConfig in items)
        {
            string[] parts = itemConfig.Split(',');
            if (parts.Length != 3) continue;

            string itemName = parts[0].Trim();
            if (!int.TryParse(parts[1].Trim(), out int stack) || !int.TryParse(parts[2].Trim(), out int price)) continue;

            ItemDrop itemPrefab = ZNetScene.instance.GetPrefab(itemName)?.GetComponent<ItemDrop>();
            if (itemPrefab == null) continue;

            tradeItems.Add(new Trader.TradeItem
            {
                m_prefab = itemPrefab,
                m_stack = stack,
                m_price = price
            });
        }
    }

    public string GetHoverText()
    {
        return "Traveling Trader\n[<color=yellow><b>E</b></color>] Interact";
    }

    public string GetHoverName()
    {
        return "Traveling Trader";
    }

    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        if (hold) return false;

        Player player = user as Player;
        if (player == null) return false;

        ShowTraderWindow();
        return true;
    }

    private void ShowTraderWindow()
    {
        StoreGui storeGui = StoreGui.instance;
        if (storeGui == null)
        {
            Debug.LogError("StoreGui instance is null. Cannot open trader window.");
            return;
        }

        Trader trader = GetComponent<Trader>();
        if (trader == null)
        {
            Debug.LogError("Trader component is missing on TravelingHaldor. Cannot open trader window.");
            return;
        }

        if (trader.m_items == null || trader.m_items.Count == 0)
        {
            Debug.LogWarning("Trader has no items configured. Ensure items are added via SetupTraderItems.");
            return;
        }

        storeGui.Show(trader); // Display the trader window
    }

    public bool UseItem(Humanoid user, ItemDrop.ItemData item)
    {
        return false; // Not used
    }
}

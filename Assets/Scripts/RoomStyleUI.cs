﻿using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class RoomStyleUI : MonoBehaviour
{
    [HideInInspector] public int index = 0;
    [HideInInspector] public RoomStyle roomStyle = null;
    [HideInInspector] public ThemeManager themeManager = null;

    public delegate void RemoveCallback(int index);
    public RemoveCallback removeCallback = null;

    [SerializeField] private InputField _nameField = null;
    [SerializeField] private Dropdown _tilesetDropdown = null;

    public void UpdateValues()
    {
        _nameField.text = roomStyle.name;

        _tilesetDropdown.ClearOptions();
        string[] tilesets = new string[themeManager.textures.Count];
        themeManager.textures.Keys.CopyTo(tilesets, 0);
        _tilesetDropdown.AddOptions(new List<string>(tilesets));
        for (int i = 0; i < tilesets.Length; i++)
        {
            if (tilesets[i] == roomStyle.tileset)
            {
                _tilesetDropdown.value = i;
                break;
            }
        }
    }

    public void NameChanged(string newName)
    {
        roomStyle.name = newName;
    }

    public void TilesetChanged(System.Int32 index)
    {
        roomStyle.tileset = _tilesetDropdown.options[index].text;
    }

    public void Remove()
    {
        if (removeCallback != null)
            removeCallback.Invoke(index);
    }
}

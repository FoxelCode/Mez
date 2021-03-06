﻿using UnityEngine;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// An object for loading in and holding assets (ruleset and textures) for themes.
/// </summary>
public class ThemeManager : MonoBehaviour
{
	private string _themePath;

	public List<string> themeNames { get; private set; }
	public MazeRuleset ruleset { get; private set; }
	public Dictionary<string, Texture2D> textures { get; private set; }

	public delegate void OnComplete();
	private OnComplete _callback = null;
	private bool _rulesetLoaded = false;
	private int _texturesLoaded = 0;
	private int _texturesToLoad = 0;

	[SerializeField] private Texture2D _defaultTexture = null;
	public Texture2D defaultTexture { get; private set; }

	public void Awake()
	{
		_themePath = Application.dataPath + "/../Themes/";

		themeNames = new List<string>();
		ruleset = null;
		textures = new Dictionary<string, Texture2D>();

		defaultTexture = _defaultTexture;

		// Enumerate themes.
		string[] themes = System.IO.Directory.GetDirectories(_themePath);
		foreach (string s in themes)
		{
			// Only store the theme's name.
			string themeName = s.Substring(s.LastIndexOf('/') + 1);
			themeNames.Add(themeName);
		}
	}

	/// <summary>
	/// Loads the assets for a theme asynchronously.
	/// </summary>
	public void LoadTheme(string themeName, OnComplete callback)
	{
		_callback = callback;

		ruleset = null;
		textures.Clear();

		LoadThemeTextures(themeName);
		LoadThemeRuleset(themeName);
	}

	public bool CreateTheme(string themeName)
	{
		if (themeNames.Contains(themeName))
			return false;
		
		Directory.CreateDirectory(_themePath + themeName);
		MazeRuleset newRuleset = new MazeRuleset();
		newRuleset.name = themeName;
		SaveThemeRuleset(newRuleset);
		themeNames.Add(themeName);
		return true;
	}

	public bool RenameTheme(string toName)
	{
		if (themeNames.Contains(toName))
			return false;
		
		string fromDir = _themePath + ruleset.name;
		string toDir = _themePath + toName;
		Directory.Move(fromDir, toDir);
		File.Move(toDir + "/" + ruleset.name + ".json", toDir + "/" + toName + ".json");
		themeNames.Remove(ruleset.name);
		themeNames.Add(toName);
		ruleset.SetName(toName);
		SaveThemeRuleset();
		return true;
	}

	private void UpdateLoadingState()
	{
		if (!_rulesetLoaded) return;
		if (_texturesLoaded < _texturesToLoad) return;

		if (_callback != null)
			_callback.Invoke();
	}

	private void LoadThemeRuleset(string themeName)
	{
		_rulesetLoaded = false;
		
		string rulesetPath = _themePath + themeName + "/" + themeName + ".json";
		if (!System.IO.File.Exists(rulesetPath))
		{
			Debug.LogWarning("Trying to load ruleset \"" + rulesetPath + "\" which doesn't exist!");
			_callback();
			return;
		}

		StartCoroutine(DoLoadThemeRuleset(rulesetPath));
	}

	private IEnumerator<WWW> DoLoadThemeRuleset(string rulesetPath)
	{
		WWW www = new WWW("file://" + rulesetPath);
		yield return www;

		ruleset = JsonUtility.FromJson<MazeRuleset>(www.text);
		ruleset.Validate(this);

		_rulesetLoaded = true;
		UpdateLoadingState();
	}

	public void SaveThemeRuleset()
	{
		SaveThemeRuleset(ruleset);
	}

	private void SaveThemeRuleset(MazeRuleset ruleset)
	{
		string rulesetPath = _themePath + ruleset.name + "/" + ruleset.name + ".json";
		string jsonString = JsonUtility.ToJson(ruleset, true);
		File.WriteAllText(rulesetPath, jsonString);
	}

	private void LoadThemeTextures(string themeName)
	{
		string[] texturePaths = System.IO.Directory.GetFiles(_themePath + themeName, "*.png");
		_texturesToLoad = texturePaths.Length;
		_texturesLoaded = 0;

		foreach (string path in texturePaths)
			LoadTexture(path, () => { _texturesLoaded++; UpdateLoadingState(); } );
	}

	public void LoadTexture(string path, OnComplete callback)
	{
		if (!System.IO.File.Exists(path))
		{
			Debug.LogWarning("Trying to load texture \"" + path + "\" which doesn't exist!");
			callback();
			return;
		}

		string textureName = Utils.ParseFileName(path);
		textures.Add(textureName, null);

		StartCoroutine(DoLoadTexture(path, callback));
	}

	private IEnumerator<WWW> DoLoadTexture(string path, OnComplete callback)
	{
		WWW www = new WWW("file://" + path);
		yield return www;

		Texture2D texture = new Texture2D(0, 0, TextureFormat.RGBA32, false, false);
		texture.anisoLevel = 0;
		texture.filterMode = FilterMode.Point;
		www.LoadImageIntoTexture(texture);

	#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
		// Turn Windows' backslashes into nice regular slashes.
		path = path.Replace('\\', '/');
	#endif
		string textureName = Utils.ParseFileName(path);
		textures[textureName] = texture;

		if (callback != null)
			callback();
	}
}
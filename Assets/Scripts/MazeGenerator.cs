﻿using UnityEngine;
using System.Collections.Generic;

public class MazeGenerator : MonoBehaviour
{
	/*
	 Editor parameters.
	 */

    /// Size of a tile in world dimensions.
	[SerializeField] private Vector2 _tileSize;
	/// Length of the entrance corridor before a maze (in tiles).
	[SerializeField] private uint _entranceLength = 0;

	[SerializeField] private GameObject _plane = null;
	[SerializeField] private GameObject _uvPlane = null;
	[SerializeField] private GameObject _corridor = null;

	[SerializeField] private Shader _regularShader = null;
	[SerializeField] private Shader _seamlessShader = null;
	[SerializeField] private Shader _transparentShader = null;

	/// Should maze generation be stepped through manually?
	[SerializeField] private bool _stepThrough = false;

	/*
	 Variables used during maze generation.
	 */

	public enum State
	{
		Idle,
		RunningSprawlers,
		AddingDecorations,
		AddingFlavourTiles,
		Finished
	}
	[SerializeField] private State _state;
	public State state { get { return _state; } private set { _state = value; } }

	public List<string> messageLog { get; private set; }

	private uint[,] _grid = null;
	private Maze _maze = null;

	private MazeRuleset _ruleset = null;
	private Dictionary<string, RoomStyle> _roomStyles = null;
	
	private int _currentSprawlerRulesetIndex = -1;
	public RoomRuleset currentSprawlerRuleset { get; private set; }
	private uint _numSprawlersRun = 0;
	private uint _numSprawlersToRun = 0;
	private uint _numSprawlersFailed = 0;
	private static uint MaxSprawlerFailures = 8;
	private Sprawler _currentSprawler = null;
	private List<Tile> _currentSprawlerTiles = null;
	private List<Tile> _newSprawlerTiles = null;

	private const float Epsilon = 0.001f;

	private ThemeManager _themeManager = null;
	private const string TilesetFloorSuffix = "_floor";
	private const string TilesetCeilingSuffix = "_ceiling";
	private Dictionary<string, Material> _materials = null;

	public delegate void OnComplete(Maze maze);
	private OnComplete _onComplete = null;

	/// Distance to the end point from the start of the maze.
	private uint _endPointDist = 0;
	private Point _endPointCoord = new Point(-1, -1);
	private Dir _endPointDir = Dir.N;

    /// <summary>
    /// Asynchronously generates a maze.
	/// Returns the finished maze as a parameter to onComplete.
    /// </summary>
	public void GenerateMaze(MazeRuleset ruleset, ThemeManager themeManager, OnComplete onComplete)
	{
		_ruleset = ruleset;
		_themeManager = themeManager;
		_onComplete = onComplete;

		_roomStyles = new Dictionary<string, RoomStyle>();
		foreach (RoomStyle tileset in ruleset.roomStyles)
			_roomStyles.Add(tileset.name, tileset);
		// If the ruleset doesn't specify a default theme, create one.
		if (!_roomStyles.ContainsKey("default"))
			_roomStyles.Add("default", new RoomStyle());

		messageLog = new List<string>();
		_newSprawlerTiles = new List<Tile>();
		_materials = new Dictionary<string, Material>();

		_endPointDist = 0;
		_endPointCoord = new Point(-1, -1);

		_grid = new uint[ruleset.size.y, ruleset.size.x];

		int startX = Random.instance.Next(ruleset.size.x);
		CarvePassagesFrom(startX, 0, _grid, 0);

		_grid[0, startX] |= Nav.bits[Dir.N];
		_grid[_endPointCoord.y, _endPointCoord.x] |= Nav.bits[_endPointDir];

        // Base GameObject for the maze.
		GameObject mazeInstance = new GameObject();
		mazeInstance.name = "Maze";
		_maze = mazeInstance.AddComponent<Maze>();
		if (_maze == null)
		{
			Debug.LogError("Maze prefab has no Maze script attached!");
			return;
		}
		_maze.Initialise((uint)ruleset.size.x, (uint)ruleset.size.y, _tileSize);
		_maze.startPosition = new Point(startX, 0);
		_maze.entranceLength = _entranceLength;

		CreateCorridors(mazeInstance);
		CreateTiles(_grid, _maze);
		CreateTileGeometry(_maze);
		TextureMaze();
		UpdateMazeUVs();

		if (ruleset.rooms != null && ruleset.rooms.Length > 0)
		{
			NextSprawlerRuleset();
			_state = State.RunningSprawlers;
		}
		else
			_state = State.AddingDecorations;
		
		if (!_stepThrough)
			while (Step()) {}
	}

	/// <summary>
	/// Finish up the maze and clean up all the generation stuff.
	/// </summary>
	private void FinishMaze()
	{
		UpdateMazeUVs();

		if (_onComplete != null)
			_onComplete.Invoke(_maze);
		state = State.Idle;

		// Remove all of the unnecessary objects.
		_grid = null;
		_maze = null;
		_ruleset = null;
		_roomStyles = null;
		currentSprawlerRuleset = null;
		_currentSprawlerTiles = null;
		_newSprawlerTiles = null;
		messageLog = null;
		_materials = null;
	}

	/// <summary>
	/// Steps the maze generation forwards. What this does depends on the state of the generation.
	/// When running sprawlers, steps the current sprawler forwards one tile.
	/// </summary>
	public bool Step()
	{
		messageLog.Clear();

		switch (state)
		{
			case State.RunningSprawlers:
				currentSprawlerRuleset = _ruleset.rooms[_currentSprawlerRulesetIndex];
				
				// Create a new Sprawler if we don't have one to Step.
				if (_currentSprawler == null)
				{
					Tile startTile = null;
					switch (currentSprawlerRuleset.start)
					{
						case RoomRuleset.Start.Start:
							startTile = _maze.GetTile(_maze.startPosition);
							break;
						case RoomRuleset.Start.Random:
							Point randomPoint = new Point(Random.instance.Next(_maze.size.x), Random.instance.Next(_maze.size.y));
							startTile = _maze.GetTile(randomPoint);
							break;
						case RoomRuleset.Start.End:
							startTile = _maze.GetTile(_endPointCoord);
							break;
					}
					if (startTile == null)
						Debug.LogError("Sprawler starting room went out of bounds!");

					_currentSprawlerTiles = new List<Tile>();

					uint sprawlerSize = 0;
					Range sprawlerSizeRange;
					currentSprawlerRuleset.TryParseSize(out sprawlerSizeRange);
					if (sprawlerSizeRange.x == sprawlerSizeRange.y)
						sprawlerSize = (uint)sprawlerSizeRange.x;
					else
						sprawlerSize = (uint)Random.instance.Next(sprawlerSizeRange.x, sprawlerSizeRange.y + 1);

					_currentSprawler = new Sprawler(_maze, startTile.position, sprawlerSize,
						(Tile tile) => { _newSprawlerTiles.Add(tile); } );
					messageLog.Add("Added sprawler at " + startTile.position.ToString());
				}

				_newSprawlerTiles.Clear();

				// Step the current Sprawler.
				bool sprawlerFinished = !_currentSprawler.Step();

				// Copy the newly added Tiles to the current Sprawler's Tiles.
				foreach (Tile tile in _newSprawlerTiles)
					_currentSprawlerTiles.Add(tile);

				if (sprawlerFinished)
				{
					if (!_currentSprawler.success)
					{
						messageLog.Add("Sprawler failed");
						_numSprawlersFailed++;
						if (_numSprawlersFailed >= MaxSprawlerFailures)
						{
							Debug.LogWarning("Maximum amount of Sprawlers failed, moving to the next ruleset.");
							NextSprawlerRuleset();
						}
					}
					else
					{
						messageLog.Add("Sprawler finished successfully");

						// Apply the new theme to the Sprawler's Tiles.
						foreach (Tile tile in _currentSprawlerTiles)
							tile.theme = _roomStyles[_ruleset.rooms[_currentSprawlerRulesetIndex].style].name;
						
						if (_stepThrough)
						{
							TextureMaze();
							UpdateMazeUVs();
						}

						// Move to the next Sprawler.
						_numSprawlersRun++;
						if (_numSprawlersRun >= _numSprawlersToRun)
						{
							// If we've run all the Sprawlers in the current SprawlerRuleset, move to the next one.
							NextSprawlerRuleset();
						}
					}
					_currentSprawler = null;
				}
				break;
			
			case State.AddingDecorations:
				foreach (RoomStyle roomStyle in _ruleset.roomStyles)
				{
					List<Tile> tiles = new List<Tile>();
					for (int y = 0; y < _maze.size.y; y++)
					{
						for (int x = 0; x < _maze.size.x; x++)
						{
							Tile tile = _maze.GetTile(x, y);
							if (tile.theme == roomStyle.name)
								tiles.Add(tile);
						}
					}

					if (roomStyle.decorations != null && roomStyle.decorations.Length > 0)
					foreach (DecorationRuleset decorationRuleset in roomStyle.decorations)
					{
						if (!_materials.ContainsKey(decorationRuleset.texture))
						{
							Material decorationMaterial = new Material(_transparentShader);
							decorationMaterial.mainTexture = _themeManager.textures[decorationRuleset.texture];
							_materials.Add(decorationRuleset.texture, decorationMaterial);
						}

						List<DecorationLocation> decorationLocations = CalculatePossibleDecorationLocations(tiles, decorationRuleset);
						Utils.Shuffle(Random.instance, decorationLocations);

						float lengthOffset = (decorationRuleset.length - 1) * 1.0f;

						switch (decorationRuleset.amountType)
						{
							case DecorationRuleset.AmountType.Chance:
								float chance = float.Parse(decorationRuleset.amount);
								foreach (DecorationLocation decorationLocation in decorationLocations)
								{
									bool validLocation = true;
									foreach (Tile tile in decorationLocation.tiles)
									{
										if (decorationRuleset.location == DecorationRuleset.Location.Floor && tile.floorDecoration
											|| decorationRuleset.location == DecorationRuleset.Location.Ceiling && tile.ceilingDecoration
											|| decorationRuleset.location == DecorationRuleset.Location.Wall && tile.wallDecorations.Contains((Dir)System.Enum.Parse(typeof(Dir), decorationLocation.location.name)))
											validLocation = false;	
									}
									if (!validLocation)
										continue;

									if (Random.YesOrNo(chance / 100.0f))
									{
										GameObject decoration = Instantiate(_plane, new Vector3(), Quaternion.identity);
										decoration.GetComponent<MeshRenderer>().material = _materials[decorationRuleset.texture];
										switch (decorationRuleset.location)
										{
											case DecorationRuleset.Location.Floor:
												decoration.transform.position += new Vector3((decorationLocation.axis == Axis.Y) ? lengthOffset : 0.0f,
													Epsilon, (decorationLocation.axis == Axis.X) ? lengthOffset : 0.0f);
												decoration.transform.rotation = Quaternion.Euler(0.0f, (decorationLocation.axis == Axis.X) ? 90.0f : 0.0f, 0.0f);
												decoration.transform.localScale = new Vector3(1.0f * decorationRuleset.length, 1.0f, 1.0f);
												decoration.transform.SetParent(decorationLocation.location.transform.parent, false);
												foreach (Tile tile in decorationLocation.tiles)
													tile.floorDecoration = true;
												break;
											case DecorationRuleset.Location.Ceiling:
												decoration.transform.position += new Vector3((decorationLocation.axis == Axis.Y) ? lengthOffset : 0.0f,
													2.0f - Epsilon, (decorationLocation.axis == Axis.X) ? lengthOffset : 0.0f);
												decoration.transform.rotation = Quaternion.Euler(0.0f, (decorationLocation.axis == Axis.X) ? 90.0f : 0.0f, 0.0f);
												decoration.transform.localScale = new Vector3(1.0f * decorationRuleset.length, -1.0f, -1.0f);
												decoration.transform.SetParent(decorationLocation.location.transform.parent, false);
												foreach (Tile tile in decorationLocation.tiles)
													tile.ceilingDecoration = true;
												break;
											case DecorationRuleset.Location.Wall:
												Dir wallDir = (Dir)System.Enum.Parse(typeof(Dir), decorationLocation.location.name);
												Axis wallAxis = (wallDir == Dir.E || wallDir == Dir.W) ? Axis.X : Axis.Y;
												decoration.transform.rotation = Quaternion.Euler(90.0f, Nav.FacingToAngle(wallDir), -90.0f);
												decoration.transform.position += new Vector3(Nav.DY[wallDir] * (_maze.tileSize.y / 2.0f - Epsilon) + ((wallAxis == Axis.X) ? lengthOffset : 0.0f),
													1.0f, Nav.DX[wallDir] * (_maze.tileSize.x / 2.0f - Epsilon) + ((wallAxis == Axis.Y) ? lengthOffset : 0.0f));
												decoration.transform.localScale = new Vector3(1.0f * decorationRuleset.length, 1.0f, 1.0f);
												decoration.transform.SetParent(decorationLocation.location.transform.parent.parent, false);
												foreach (Tile tile in decorationLocation.tiles)
													tile.wallDecorations.Add(wallDir);
												break;
										}
									}
								}
								break;
							
							case DecorationRuleset.AmountType.Count:
								Range countRange;
								decorationRuleset.TryParseCount(out countRange);
								if (tiles.Count < countRange.x)
								{
									Debug.LogWarning("Not enough tiles of style \"" + roomStyle.name + "\" to satisfy decoration count range. (requires at least " + countRange.x + ")");
									continue;
								}

								int decorationCount = Random.instance.Next(countRange.x, Mathf.Min(countRange.y, decorationLocations.Count) + 1);
								for (int i = 0; i < decorationCount; i++)
								{
									bool validLocation = true;
									foreach (Tile tile in decorationLocations[i].tiles)
									{
										if (decorationRuleset.location == DecorationRuleset.Location.Floor && tile.floorDecoration
											|| decorationRuleset.location == DecorationRuleset.Location.Ceiling && tile.ceilingDecoration
											|| decorationRuleset.location == DecorationRuleset.Location.Wall && tile.wallDecorations.Contains((Dir)System.Enum.Parse(typeof(Dir), decorationLocations[i].location.name)))
											validLocation = false;	
									}
									if (!validLocation)
										continue;

									GameObject decoration = Instantiate(_plane, new Vector3(), Quaternion.identity);
									decoration.GetComponent<MeshRenderer>().material = _materials[decorationRuleset.texture];
									switch (decorationRuleset.location)
									{
										case DecorationRuleset.Location.Floor:
											decoration.transform.position += new Vector3((decorationLocations[i].axis == Axis.Y) ? lengthOffset : 0.0f,
													Epsilon, (decorationLocations[i].axis == Axis.X) ? lengthOffset : 0.0f);
												decoration.transform.rotation = Quaternion.Euler(0.0f, (decorationLocations[i].axis == Axis.X) ? 90.0f : 0.0f, 0.0f);
												decoration.transform.localScale = new Vector3(1.0f * decorationRuleset.length, 1.0f, 1.0f);
											decoration.transform.SetParent(decorationLocations[i].location.transform.parent, false);
											foreach (Tile tile in decorationLocations[i].tiles)
												tile.floorDecoration = true;
											break;
										case DecorationRuleset.Location.Ceiling:
											decoration.transform.position += new Vector3((decorationLocations[i].axis == Axis.Y) ? lengthOffset : 0.0f,
													2.0f - Epsilon, (decorationLocations[i].axis == Axis.X) ? lengthOffset : 0.0f);
												decoration.transform.rotation = Quaternion.Euler(0.0f, (decorationLocations[i].axis == Axis.X) ? 90.0f : 0.0f, 0.0f);
												decoration.transform.localScale = new Vector3(1.0f * decorationRuleset.length, -1.0f, -1.0f);
											decoration.transform.SetParent(decorationLocations[i].location.transform.parent, false);
											break;
										case DecorationRuleset.Location.Wall:
											Dir wallDir = (Dir)System.Enum.Parse(typeof(Dir), decorationLocations[i].location.name);
											Axis wallAxis = (wallDir == Dir.E || wallDir == Dir.W) ? Axis.X : Axis.Y;
											decoration.transform.rotation = Quaternion.Euler(90.0f, Nav.FacingToAngle(wallDir), -90.0f);
											decoration.transform.position += new Vector3(Nav.DY[wallDir] * (_maze.tileSize.y / 2.0f - Epsilon) + ((wallAxis == Axis.X) ? lengthOffset : 0.0f),
												1.0f, Nav.DX[wallDir] * (_maze.tileSize.x / 2.0f - Epsilon) + ((wallAxis == Axis.Y) ? lengthOffset : 0.0f));
											decoration.transform.localScale = new Vector3(1.0f * decorationRuleset.length, 1.0f, 1.0f);
											decoration.transform.SetParent(decorationLocations[i].location.transform.parent.parent, false);
											break;
									}
								}
								break;
						}
					}
				}
				_state = State.AddingFlavourTiles;
				break;
			
			case State.AddingFlavourTiles:
				foreach (RoomStyle roomStyle in _ruleset.roomStyles)
				{
					List<Tile> tiles = new List<Tile>();
					for (int y = 0; y < _maze.size.y; y++)
					{
						for (int x = 0; x < _maze.size.x; x++)
						{
							Tile tile = _maze.GetTile(x, y);
							if (tile.theme == roomStyle.name)
								tiles.Add(tile);
						}
					}

					if (roomStyle.flavourTiles != null && roomStyle.flavourTiles.Length > 0)
					foreach (FlavourTileRuleset flavourTileRuleset in roomStyle.flavourTiles)
					{
						if (!_materials.ContainsKey(flavourTileRuleset.texture))
						{
							Material flavourTileMaterial = new Material(_seamlessShader);
							flavourTileMaterial.mainTexture = _themeManager.textures[flavourTileRuleset.texture];
							_materials.Add(flavourTileRuleset.texture, flavourTileMaterial);
						}

						List<Tile> flavourTiles = new List<Tile>();
						foreach (Tile tile in tiles)
						{
							if (!_maze.IsTileValid(tile.position, flavourTileRuleset.validLocations))
								continue;
							flavourTiles.Add(tile);
						}
						Utils.Shuffle(Random.instance, flavourTiles);

						switch (flavourTileRuleset.amountType)
						{
							case FlavourTileRuleset.AmountType.Chance:
								float chance = float.Parse(flavourTileRuleset.amount);
								foreach (Tile tile in flavourTiles)
								{
									if (Random.YesOrNo(chance / 100.0f))
									{
										if (Utils.IsBitUp(flavourTileRuleset.location, (byte)FlavourTileRuleset.Location.Floor))
											TextureTileFloor(tile, flavourTileRuleset.texture);
										if (Utils.IsBitUp(flavourTileRuleset.location, (byte)FlavourTileRuleset.Location.Wall))
											TextureTileWalls(tile, flavourTileRuleset.texture);
										if (Utils.IsBitUp(flavourTileRuleset.location, (byte)FlavourTileRuleset.Location.Ceiling))
											TextureTileCeiling(tile, flavourTileRuleset.texture);
									}
								}
								break;
							
							case FlavourTileRuleset.AmountType.Count:
								Range countRange;
								flavourTileRuleset.TryParseCount(out countRange);
								if (flavourTiles.Count < countRange.x)
								{
									Debug.LogWarning("Not enough tiles of style \"" + roomStyle.name + "\" to satisfy decoration count range. (requires at least " + countRange.x + ")");
									continue;
								}

								int decorationCount = Random.instance.Next(countRange.x, Mathf.Min(countRange.y, flavourTiles.Count) + 1);
								for (int i = 0; i < decorationCount; i++)
								{
									Tile tile = flavourTiles[i];

									if (Utils.IsBitUp(flavourTileRuleset.location, (byte)FlavourTileRuleset.Location.Floor))
										TextureTileFloor(tile, flavourTileRuleset.texture);
									if (Utils.IsBitUp(flavourTileRuleset.location, (byte)FlavourTileRuleset.Location.Wall))
										TextureTileWalls(tile, flavourTileRuleset.texture);
									if (Utils.IsBitUp(flavourTileRuleset.location, (byte)FlavourTileRuleset.Location.Ceiling))
										TextureTileCeiling(tile, flavourTileRuleset.texture);
								}
								break;
						}
					}
				}
				_state = State.Finished;
				break;

			case State.Finished:
				FinishMaze();
				return false;
		}
		return true;
	}

	/// <summary>
	/// Moves to the next sprawler ruleset.
	/// Stops running sprawlers if all rulesets have been run.
	/// </summary>
	private void NextSprawlerRuleset()
	{
		_numSprawlersRun = 0;
		_numSprawlersFailed = 0;
		_currentSprawlerRulesetIndex++;
		if (_currentSprawlerRulesetIndex >= _ruleset.rooms.GetLength(0))
		{
			// If we've run all the SprawlerRulesets in the MazeRuleset, move to the next state.
			_currentSprawlerRulesetIndex = -1;
			state = State.AddingDecorations;
			TextureMaze();
		}
		else
		{
			Range sprawlerCountRange;
			_ruleset.rooms[_currentSprawlerRulesetIndex].TryParseCount(out sprawlerCountRange);
			if (sprawlerCountRange.x == sprawlerCountRange.y)
				_numSprawlersToRun = (uint)sprawlerCountRange.x;
			else
				_numSprawlersToRun = (uint)Random.instance.Next(sprawlerCountRange.x, sprawlerCountRange.y + 1);
		}
	}

    /// <summary>
    /// Generates a maze into a 2D array.
    /// Calls itself recursively until the maze is complete.
    /// </summary>
    /// <param name="distance">Distance from the beginning of the maze (in tiles).</param>
	private void CarvePassagesFrom(int x, int y, uint[,] grid, uint distance)
	{
        // Try moving in directions in a random order.
		List<Dir> directions = new List<Dir> { Dir.N, Dir.S, Dir.E, Dir.W };
		Utils.Shuffle(Random.instance, directions);

		foreach (Dir dir in directions)
		{
            // Calculate new index position in the direction to try to move in.
			int nx = x + Nav.DX[dir];
			int ny = y + Nav.DY[dir];

            // Check that the new position is within the maze dimensions.
			if ((ny >= 0 && ny < grid.GetLength(0)) && (nx >= 0 && nx < grid.GetLength(1)) && (grid[ny, nx] == 0))
			{
                // Set the connection bits in this new tile and the tile we came from.
				grid[y, x] |= Nav.bits[dir];
				grid[ny, nx] |= Nav.bits[Nav.opposite[dir]];

                // Continue generating the maze.
				CarvePassagesFrom(nx, ny, grid, distance + 1);
			}
		}

		// Check if the current position could be an endpoint.
		if (distance > _endPointDist)
		{
			List<Dir> possibleEndPointDirs = new List<Dir>();
			if (x == 0)							possibleEndPointDirs.Add(Dir.W);
			if (y == 0)							possibleEndPointDirs.Add(Dir.N);
			if (x == (grid.GetLength(1) - 1))	possibleEndPointDirs.Add(Dir.E);
			if (y == (grid.GetLength(0) - 1))	possibleEndPointDirs.Add(Dir.S);

			if (possibleEndPointDirs.Count > 0)
			{
				_endPointCoord.Set(x, y);
				_endPointDist = distance;

				Utils.Shuffle(Random.instance, possibleEndPointDirs);
				_endPointDir = possibleEndPointDirs[0];
			}
		}
	}

    /// <summary>
    /// Creates tiles into a Maze according to the bitwise data in a given 2D array.
    /// </summary>
	private void CreateTiles(uint[,] grid, Maze maze)
	{
		for (uint y = 0; y < grid.GetLength(0); y++)
		{
			for (uint x = 0; x < grid.GetLength(1); x++)
			{
                // Create the tile and add it to the Maze.
				Tile tile = new Tile(grid[y, x], new Point((int)x, (int)y));
				tile.theme = "default";
				maze.AddTile(tile);
			}
		}
	}

	/// <summary>
	/// Creates tile geometries for all the tiles in a maze.
	/// </summary>
	private void CreateTileGeometry(Maze maze)
	{
		for (int y = 0; y < maze.size.y; y++)
		{
			for (int x = 0; x < maze.size.x; x++)
			{
				Tile tile = maze.GetTile(x, y);

				tile.instance.transform.position = new Vector3(y * _tileSize.y, 0.0f, x * _tileSize.x);

				// Create the floor.
				GameObject floorInstance = (GameObject)Instantiate(_uvPlane,
					new Vector3(),
					Quaternion.Euler(0.0f, Autotile.tileRotations[tile.value], 0.0f));
				floorInstance.name = "Floor";
				floorInstance.transform.SetParent(tile.instance.transform, false);
				tile.floor = floorInstance;

				// Create the ceiling.
				GameObject ceilingInstance = (GameObject)Instantiate(_uvPlane,
					new Vector3(0.0f, 2.0f, 0.0f),
					Quaternion.Euler(0.0f, Autotile.tileRotations[tile.value], 0.0f));
				ceilingInstance.transform.localScale = new Vector3(1.0f, -1.0f, 1.0f);
				ceilingInstance.name = "Ceiling";
				ceilingInstance.transform.SetParent(tile.instance.transform, false);
				tile.ceiling = ceilingInstance;

				// Create the walls.
				GameObject wallsInstance = new GameObject("Walls");
				wallsInstance.transform.SetParent(tile.instance.transform, false);

				// Create a wall in every direction the tile isn't connected in.
				List<GameObject> walls = new List<GameObject>();
				foreach (Dir dir in System.Enum.GetValues(typeof(Dir)))
				{
					if (!Nav.IsConnected(tile.value, dir))
					{
						GameObject wallInstance = (GameObject)Instantiate(_uvPlane, new Vector3(0.0f, 1.0f, 0.0f),
								Quaternion.Euler(0.0f, Nav.FacingToAngle(dir), -90.0f));
						wallInstance.transform.SetParent(wallsInstance.transform, false);
						wallInstance.transform.position += new Vector3(Nav.DY[dir] * (maze.tileSize.y / 2.0f), 0.0f, Nav.DX[dir] * (maze.tileSize.x / 2.0f));
						wallInstance.name = dir.ToString();
						walls.Add(wallInstance);
					}
				}
				tile.walls = walls.ToArray();
			}
		}
	}

	/// <summary>
	/// Creates entrance and exit corridors for a maze.
	/// </summary>
	private void CreateCorridors(GameObject mazeInstance)
	{
		GameObject entrance = Instantiate(_corridor, _maze.TileToWorldPosition(_maze.startPosition) - new Vector3(_maze.tileSize.y / 2.0f, 0.0f, 0.0f), Quaternion.identity, mazeInstance.transform);
		entrance.transform.localScale = new Vector3(_entranceLength, 1.0f, 1.0f);
		entrance.name = "Entrance";

		GameObject exit = Instantiate(_corridor,
			_maze.TileToWorldPosition(_endPointCoord) + new Vector3(Nav.DY[_endPointDir] * (_maze.tileSize.y / 2.0f), 0.0f, Nav.DX[_endPointDir] * (_maze.tileSize.x / 2.0f)),
			Quaternion.Euler(0.0f, Nav.FacingToAngle(_endPointDir), 0.0f), mazeInstance.transform);
		exit.transform.localScale = new Vector3(_entranceLength, 1.0f, 1.0f);
		exit.name = "Exit";
	}

	/// <summary>
	/// Updates the textures of all tiles in the maze.
	/// </summary>
	private void TextureMaze()
	{
		for (int y = 0; y < _maze.size.y; y++)
		{
			for (int x = 0; x < _maze.size.x; x++)
			{
				TextureTile(_maze.GetTile(x, y));
			}
		}
	}

	/// <summary>
	/// Updates a given tile's textures to those of its theme.
	/// </summary>
	private void TextureTile(Tile tile)
	{
		TextureTile(tile, _roomStyles[tile.theme].tileset);
	}
	
	/// <summary>
	/// Sets the textures of a tile to a given tileset.
	/// </summary>
	private void TextureTile(Tile tile, string tilesetName)
	{
		TextureTileWalls(tile, tilesetName);
		TextureTileFloor(tile, tilesetName);
		TextureTileCeiling(tile, tilesetName);
	}

	/// <summary>
	/// Sets the floor texture of a tile to a given tileset.
	/// </summary>
	private void TextureTileFloor(Tile tile, string tilesetName)
	{
		string floorTilesetName = tilesetName + TilesetFloorSuffix;

		if (!_materials.ContainsKey(floorTilesetName))
		{
			Material floorMaterial = _materials[tilesetName];
			// Create a seamless material for the floor if one exists.
			if (_themeManager.textures.ContainsKey(floorTilesetName))
			{
				floorMaterial = new Material(_seamlessShader);
				floorMaterial.mainTexture = _themeManager.textures[tilesetName];
				floorMaterial.SetTexture("_SeamlessTex", _themeManager.textures[floorTilesetName]);
				floorMaterial.SetTextureScale("_SeamlessTex", new Vector2(1.0f / _tileSize.x, 1.0f / _tileSize.y));
			}
			_materials.Add(floorTilesetName, floorMaterial);
		}

		tile.floor.GetComponent<MeshRenderer>().material = _materials[floorTilesetName];
	}

	/// <summary>
	/// Sets the wall textures of a tile to a given tileset.
	/// </summary>
	private void TextureTileWalls(Tile tile, string tilesetName)
	{
		if (!_materials.ContainsKey(tilesetName))
		{
			Material regularMaterial = new Material(_regularShader);

			// Use the tile's tileset if it's loaded.
			if (_themeManager.textures.ContainsKey(tilesetName))
			{
				regularMaterial.mainTexture = _themeManager.textures[tilesetName];
			}
			// If the tile's tileset isn't loaded, use the default one.
			else
			{
				regularMaterial.mainTexture = _themeManager.defaultTexture;
				if (tilesetName != "default")
					Debug.LogWarning("Tried using tileset called \"" + tilesetName + "\" but it isn't loaded, using the default tileset.", tile.instance);
			}
			_materials.Add(tilesetName, regularMaterial);
		}

		foreach (GameObject wall in tile.walls)
			wall.GetComponent<MeshRenderer>().material = _materials[tilesetName];
	}

	/// <summary>
	/// Sets the ceiling texture of a tile to a given tileset.
	/// </summary>
	private void TextureTileCeiling(Tile tile, string tilesetName)
	{
		string ceilingTilesetName = tilesetName + TilesetCeilingSuffix;

		if (!_materials.ContainsKey(ceilingTilesetName))
		{
			Material ceilingMaterial = _materials[tilesetName];
			// Create a seamless material for the ceiling if one exists.
			if (_themeManager.textures.ContainsKey(ceilingTilesetName))
			{
				ceilingMaterial = new Material(_seamlessShader);
				ceilingMaterial.mainTexture = _themeManager.textures[tilesetName];
				ceilingMaterial.SetTexture("_SeamlessTex", _themeManager.textures[ceilingTilesetName]);
				ceilingMaterial.SetTextureScale("_SeamlessTex", new Vector2(1.0f / _tileSize.x, 1.0f / _tileSize.y));
			}
			_materials.Add(ceilingTilesetName, ceilingMaterial);
		}

		tile.ceiling.GetComponent<MeshRenderer>().material = _materials[ceilingTilesetName];
	}

	/// <summary>
	/// Updates the UVs of all tiles in the maze by autotiling them.
	/// </summary>
	private void UpdateMazeUVs()
	{
		for (int y = 0; y < _maze.size.y; y++)
		{
			for (int x = 0; x < _maze.size.x; x++)
			{
				UpdateTileUV(_maze.GetTile(x, y));
			}
		}
	}

	/// <summary>
	/// Updates the UVs of a tile by autotiling it.
	/// </summary>
	private void UpdateTileUV(Tile tile)
	{
		uint fixedValue = _maze.GetGraphicalTileValue(tile);

		// Update the floor's UVs.
		tile.floor.GetComponent<UVRect>().offset = Autotile.GetUVOffsetByIndex(Autotile.floorTileStartIndex + Autotile.fourBitTileIndices[fixedValue]);
		tile.floor.transform.rotation = Quaternion.Euler(0.0f, Autotile.tileRotations[fixedValue], 0.0f);

		// Update the ceiling's UVs.
		tile.ceiling.GetComponent<UVRect>().offset = Autotile.GetUVOffsetByIndex(Autotile.ceilingTileStartIndex + Autotile.fourBitTileIndices[fixedValue]);
		tile.ceiling.transform.rotation = Quaternion.Euler(0.0f, Autotile.tileRotations[fixedValue], 0.0f);

		// Update the walls' UVs.
		foreach (GameObject wall in tile.walls)
		{
			Dir wallDir = (Dir)System.Enum.Parse(typeof(Dir), wall.name);
			uint wallValue = 0;
			if (Nav.IsConnected(fixedValue, Nav.left[wallDir]))
			{
				Tile leftTile = _maze.GetTile(tile.position + new Point(Nav.DX[Nav.left[wallDir]], Nav.DY[Nav.left[wallDir]]));
				if (leftTile != null)
				{
					if (Autotile.IsWallConnected(fixedValue, leftTile.value, wallDir))
						wallValue |= 1;
				}
			}
			if (Nav.IsConnected(fixedValue, Nav.right[wallDir]))
			{
				Tile rightTile = _maze.GetTile(tile.position + new Point(Nav.DX[Nav.right[wallDir]], Nav.DY[Nav.right[wallDir]]));
				if (rightTile != null)
				{
					if (Autotile.IsWallConnected(fixedValue, rightTile.value, wallDir))
						wallValue |= 2;
				}
			}
			wall.GetComponent<UVRect>().offset = Autotile.GetUVOffsetByIndex(Autotile.wallTileStartIndex + Autotile.twoBitTileIndices[wallValue]);
		}
	}

	private struct TileLine
	{
		public Axis axis;
		public List<Tile> tiles;

		public TileLine(Axis axis)
		{
			this.axis = axis;
			this.tiles = new List<Tile>();
		}
	}

	private class WallLine
	{
		public struct Wall
		{
			public Tile tile;
			public GameObject wall;

			public Wall(Tile tile, GameObject wall)
			{
				this.tile = tile;
				this.wall = wall;
			}
		}

		public List<Wall> walls = new List<Wall>();
	}

	private struct DecorationLocation
	{
		public Axis axis;
		public GameObject location;
		public Tile[] tiles;

		public DecorationLocation(Axis axis, GameObject location, Tile[] tiles)
		{
			this.axis = axis;
			this.location = location;
			this.tiles = tiles;
		}
	}

	private List<DecorationLocation> CalculatePossibleDecorationLocations(List<Tile> themeTiles, DecorationRuleset ruleset)
	{
		List<DecorationLocation> decorationLocations = new List<DecorationLocation>();
		// Single tile decoration.
		if (ruleset.length == 1)
		{
			foreach (Tile tile in themeTiles)
			{
				if (!_maze.IsTileValid(tile.position, ruleset.validLocations))
					continue;

				if (ruleset.location == DecorationRuleset.Location.Wall)
					foreach (GameObject wall in tile.walls)
						decorationLocations.Add(new DecorationLocation(Axis.X, wall, new Tile[] { tile }));
				else
					decorationLocations.Add(new DecorationLocation(Axis.X, tile.floor, new Tile[] { tile }));
			}
		}
		// Multi-tile decoration.
		else
		{
			if (ruleset.location == DecorationRuleset.Location.Wall)
			{
				List<WallLine> wallLines = new List<WallLine>();
				foreach (Tile tile in themeTiles)
				{
					if (!_maze.IsTileValid(tile.position, ruleset.validLocations))
						continue;
					
					List<Tile> neighbours = _maze.GetNeighbours(tile);
					foreach (GameObject wall in tile.walls)
					{
						Dir wallDir = (Dir)System.Enum.Parse(typeof(Dir), wall.name);
						bool lineExists = false;
						foreach (WallLine line in wallLines)
						{
							foreach (WallLine.Wall lineWall in line.walls)
							{
								Dir lineWallDir = (Dir)System.Enum.Parse(typeof(Dir), lineWall.wall.name);
								if (neighbours.Contains(lineWall.tile) && lineWallDir == wallDir)
								{
									lineExists = true;
									line.walls.Add(new WallLine.Wall(tile, wall));
									break;
								}
							}
							if (lineExists)
								break;
						}
						if (!lineExists)
						{
							WallLine newLine = new WallLine();
							newLine.walls.Add(new WallLine.Wall(tile, wall));
							wallLines.Add(newLine);
						}
					}
				}

				foreach (WallLine line in wallLines)
				{
					int locations = line.walls.Count - ruleset.length + 1;
					if (locations > 0)
					{
						for (int i = 0; i < locations; i++)
						{
							Tile[] decorationTiles = new Tile[ruleset.length];
							for (int j = 0; j < ruleset.length; j++)
								decorationTiles[j] = line.walls[i + j].tile;
							DecorationLocation decorationLocation = new DecorationLocation(Axis.X, line.walls[i].wall, decorationTiles);
							decorationLocations.Add(decorationLocation);
						}
					}
				}
			}
			else
			{
				List<TileLine> tileLines = new List<TileLine>();
				foreach (Tile tile in themeTiles)
				{
					if (!_maze.IsTileValid(tile.position, ruleset.validLocations))
						continue;

					List<Dir> connections = _maze.GetConnections(tile);
					foreach (Dir dir in connections)
					{
						Tile neighbour = _maze.GetTile(tile.position + new Point(Nav.DX[dir], Nav.DY[dir]));
						Axis lineAxis = (dir == Dir.E || dir == Dir.W) ? Axis.X : Axis.Y;
						bool lineExists = false;
						foreach (TileLine line in tileLines)
						{
							if (line.axis == lineAxis)
							{
								foreach (Tile lineTile in line.tiles)
								{
									if (lineTile == neighbour)
									{
										lineExists = true;
										line.tiles.Add(tile);
										break;
									}
								}
							}
							if (lineExists)
								break;
						}
						if (!lineExists)
						{
							TileLine newLine = new TileLine(lineAxis);
							newLine.tiles.Add(tile);
							tileLines.Add(newLine);
						}
					}
				}

				foreach (TileLine line in tileLines)
				{
					int locations = line.tiles.Count - ruleset.length + 1;
					if (locations > 0)
					{
						for (int i = 0; i < locations; i++)
						{
							Tile[] decorationTiles = new Tile[ruleset.length];
							for (int j = 0; j < ruleset.length; j++)
								decorationTiles[j] = line.tiles[i + j];
							DecorationLocation decorationLocation = new DecorationLocation(line.axis, line.tiles[i].floor, decorationTiles);
							decorationLocations.Add(decorationLocation);
						}
					}
				}
			}
		}

		return decorationLocations;
	}

#if DEBUG
	private void OnDrawGizmos()
	{
		if (_newSprawlerTiles != null)
		{
			foreach (Tile tile in _newSprawlerTiles)
			{
				Gizmos.color = new Color(0.5f, 0.9f, 0.5f, 0.5f);
				Gizmos.DrawCube(Nav.TileToWorldPos(tile.position, _maze.tileSize) + new Vector3(0.0f, 1.0f, 0.0f), new Vector3(_maze.tileSize.x, 2.0f, _maze.tileSize.y));
			}
		}
		if (_currentSprawler != null)
		{
			foreach (Crawler c in _currentSprawler.crawlers)
			{
				Vector3 crawlerPosition = Nav.TileToWorldPos(c.position, _maze.tileSize) + new Vector3(0.0f, 1.0f, 0.0f);

				Gizmos.color = Color.red;
				Gizmos.DrawSphere(crawlerPosition, 0.2f);

				Gizmos.color = Color.cyan;
				Gizmos.DrawLine(crawlerPosition, crawlerPosition + new Vector3(Nav.DY[c.nextFacing], 0.0f, Nav.DX[c.nextFacing]));
			}
		}
	}
#endif
}

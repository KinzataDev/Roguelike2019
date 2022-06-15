﻿using System;
using System.Collections.Generic;
using System.Linq;

[Serializable]
public class Level
{
    public string seed;

    // Maps
    public EntityMap _entityMap;
    public EntityMap _entityMapBackground;
    public GroundMap _groundMap;
    public MiscMap _miscMap;

    // Actors
    public List<Actor> _actors;
    public Entity _player;

    [NonSerialized]
    public LevelDataScriptableObject levelData;

    public void Update()
    {
        // Handle User Input (yes we're doing this elsewhere too, plan on fixing that)

    }

    public Level BuildLevel(LevelDataScriptableObject levelData, string seed = "")
    {
        if (string.IsNullOrWhiteSpace(seed))
        {
            seed = DateTime.UtcNow.Ticks.ToString();
        }

        this.seed = seed;

        var ranGen = new Random(seed.GetHashCode());

        var levelBuilder = new LevelBuilder();
        this.levelData = levelData;

        levelBuilder.GenerateMap(levelData, ranGen);
        levelBuilder.GenerateEntities(levelData, ranGen);

        _groundMap = levelBuilder.GetGroundMap();
        _entityMap = levelBuilder.GetEntityMap();
        _entityMapBackground = levelBuilder.GetPassiveEntityMap();
        _miscMap = levelBuilder.GetMiscMap();
        _actors = levelBuilder.GetActors();

        RunVisibilitySystem();

        return this;
    }

    public void FinalSetup()
    {
        RunVisibilitySystem();
        OnTurnSuccess();
    }

    public MapDTO GetMapDTO()
    {
        return new MapDTO { EntityMap = _entityMap, EntityFloorMap = _entityMapBackground, GroundMap = _groundMap, MiscMap = _miscMap };
    }

    public List<Actor> GetActors()
    {
        return _actors;
    }

    public void OnTurnSuccess()
    {
        _groundMap.UpdateTiles();
    }

    public void SetPlayer(Entity player)
    {
        _player = player;
        _actors.Add(new Actor(_player));
        _entityMap.AddEntity(_player);
    }

    public Entity GetPlayer()
    {
        return _player;
    }

    public CellPosition GetEntryPosition()
    {
        return _groundMap.rooms.First().center;
    }

    public void RunVisibilitySystem()
    {
        _entityMapBackground.RenderAll();
        _entityMap.RenderAll();
    }

    public ActionResult HandleDeadEntities(Entity[] deadEntities)
    {
        var actionResult = new ActionResult();
        foreach (var dead in deadEntities)
        {
            if (dead == _player)
            {
                actionResult.Append(dead.ConvertToDeadPlayer());
                actionResult.TransitionToStateOnSuccess = GameState.Global_PlayerDead;
            }
            else
            {
                actionResult.Append(dead.ConvertToDeadMonster());
            }

            _entityMap.SwapEntityToMap(dead, _entityMapBackground);
            _actors.Remove(dead.actor);
        }
        return actionResult;
    }

    private Dictionary<int, List<int>> GetExploredTileCoordinates()
    {
        var exploredCoords = new Dictionary<int, List<int>>();
        foreach (var t in _groundMap.tiles)
        {
            if (t.isExplored)
            {
                if( !exploredCoords.ContainsKey(t.x))
                {
                    exploredCoords[t.x] = new List<int>();
                }

                exploredCoords[t.x].Add(t.y);
            }
        }

        return exploredCoords;
    }
    public SaveData SaveGameState()
    {
        var saveData = new SaveData
        {
            seed = seed,
            playerIndexInActors = _actors.FindIndex((a) => a == _player.actor),
            actors = _actors.Select(a => a.SaveGameState()).ToList(),
            items = _entityMapBackground.GetEntities().Select(e => e.SaveGameState()).ToList(),
            exploredTileCoordinates = GetExploredTileCoordinates()
        };

        return saveData;
    }

    public void LoadGameState(SaveData data, LevelDataScriptableObject levelData)
    {
        var ranGen = new Random(data.seed.GetHashCode());
        seed = data.seed;

        var levelBuilder = new LevelBuilder();
        this.levelData = levelData;
        levelBuilder.GenerateMap(levelData, ranGen);

        _groundMap = levelBuilder.GetGroundMap();
        _entityMap = levelBuilder.GetEntityMap();
        _entityMapBackground = levelBuilder.GetPassiveEntityMap();
        _miscMap = levelBuilder.GetMiscMap();

        var dto = GetMapDTO();

        // Load Actors and Items
        var loadedActors = data.actors.Select(a => Actor.LoadGameState(a)).ToList();
        foreach(var a in loadedActors)
        {
            _entityMap.AddEntity(a.entity);
        }
        _actors = loadedActors;

        var loadedItems = data.items.Select(e => Entity.LoadGameState(e)).ToList();
        foreach (var a in loadedItems)
        {
            _entityMapBackground.AddEntity(a);
        }

        _player = _actors[data.playerIndexInActors].entity;

        foreach ( var kvp in data.exploredTileCoordinates )
        {
            var x = (int)kvp.Key;
            foreach ( var y in kvp.Value )
            {
                _groundMap.tiles[x, y].isExplored = true;
            }
        }

        RunVisibilitySystem();
    }

    [Serializable]
    public class SaveData
    {
        public string seed;
        public int playerIndexInActors;
        public List<Actor.SaveData> actors;
        public List<Entity.SaveData> items;
        public Dictionary<int, List<int>> exploredTileCoordinates;
    }
}

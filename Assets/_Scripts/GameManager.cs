﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using UnityEngine.Tilemaps;

public class GameManager : MonoBehaviour
{
    public PlayerStatInterface statText;
    public InventoryInterface inventoryInterface;
    private MessageLog _log;
    private Action _deferredAction;
    private GameState _gameState;
    
    [Header("Systems")]
    private FieldOfViewSystem fovSystem;

    [Header("Settings")]
    public float cameraAdjustmentPercent = 0.793f;
    private float calculatedCamerageAdjustment = 0;
    public float viewportWidth = 10f;
    public int playerViewDistance = 10;

    [Header("Instance Data")]
    private int _currentActorId = 0;

    public Level currentLevel;
    public LevelDataScriptableObject levelData;


    void Start()
    {
        Application.targetFrameRate = 120;
        QualitySettings.vSyncCount = 0;

        // Should load a save file?
        var saveFileString = GameSceneManager.Instance.SaveFile;
        if( !string.IsNullOrWhiteSpace(saveFileString) )
        {
            // Load game
        }
        else
        {
            InitNewGame();
        }

    }

    public void InitNewGame()
    {
        currentLevel = new Level();
        currentLevel.BuildLevel(levelData);

        // Build Player
        var player = Entity.CreateEntity().Init(currentLevel.GetEntryPosition().Clone(), spriteType: SpriteType.Soldier_Sword, color: Color.green, name: "player", blocks: true);
        player.gameObject.AddComponent<Player>().owner = player;
        player.gameObject.AddComponent<Fighter>().Init(30, 2, 5).owner = player;
        player.gameObject.AddComponent<Inventory>().Init(capacity: 10).owner = player;

        currentLevel.SetPlayer(player);

        CalculateCameraAdjustment();
        SetDesiredScreenSize();
        Camera.main.transform.position = new Vector3(player.position.x + calculatedCamerageAdjustment, player.position.y, Camera.main.transform.position.z);

        // Setup Systems
        fovSystem = new FieldOfViewSystem(currentLevel.GetMapDTO().GroundMap);
        fovSystem.Run(new Vector2Int(player.position.x, player.position.y), playerViewDistance);

        statText.SetPlayer(player);
        inventoryInterface.SetInventory(player.GetComponent<Inventory>());

        _gameState = GameState.Global_LevelScene;
        _log = FindObjectOfType<MessageLog>();

        currentLevel.FinalSetup();
    }

    void Update()
    {
        if( _gameState == GameState.Global_PlayerDead)
        {
            // Enable Game over screen or change states
        }

        var actor = currentLevel.GetActors().ElementAt(_currentActorId);
        if( actor.entity == currentLevel.GetPlayer())
        {
            HandleUserInput();
            var turnResults = ProcessTurn();
            ProcessTurnResults(turnResults);
        }
        else
        {
            for(int i = 1; i <= 5; i++)
            {
                actor = currentLevel.GetActors().ElementAt(_currentActorId);
                if (actor.entity == currentLevel.GetPlayer())
                {
                    break;
                }
                var turnResults = ProcessTurn();
                ProcessTurnResults(turnResults);
            }
        }




        //currentLevel.Update();
    }

    ActionResult ProcessTurn()
    {
        // The deferred action exists because something in the main loop needs to happen.
        if (_deferredAction != null) { return new ActionResult(); }

        ActionResult actionResult;
        var actor = currentLevel.GetActors().ElementAt(_currentActorId);
        var action = actor.GetAction(currentLevel.GetMapDTO());
        var actionToTake = action;
        if (action == null) { return new ActionResult(); }

        var player = currentLevel.GetPlayer();

        do
        {
            actionResult = actionToTake.PerformAction(currentLevel.GetMapDTO());

            if(actor.entity == player)
            {
                // Cleanup to handle after player potentially changes position
                Camera.main.transform.position = new Vector3(player.position.x + calculatedCamerageAdjustment, player.position.y, Camera.main.transform.position.z);
            }

            if (actionResult.status == ActionResultType.Success || actionResult.status == ActionResultType.TurnDeferred || actionResult.status == ActionResultType.RepeatNextTurn)
            {
                TransitionFrom(_gameState);
                TransitionTo(actionResult.TransitionToStateOnSuccess);
            }

            actionToTake = actionResult.NextAction;
        }
        while (actionResult.NextAction != null && actionResult.status != ActionResultType.TurnDeferred);

        if( actionResult.status == ActionResultType.RepeatNextTurn)
        {
            actor.SetNextAction(action);
            actionResult.status = ActionResultType.Success;
        }

        if (actionResult.status == ActionResultType.Success)
        {
            _currentActorId = (_currentActorId + 1) % currentLevel.GetActors().Count();
            if (actor.entity == player)
            {
                fovSystem.Run(new Vector2Int(player.position.x, player.position.y), playerViewDistance);
                currentLevel.OnTurnSuccess();
            }
            if( currentLevel.GetActors().ElementAt(_currentActorId).entity == player)
            {
                currentLevel.RunVisibilitySystem();
            }
        }

        if (actionResult.status == ActionResultType.TurnDeferred)
        {
            _deferredAction = actionToTake;
        }

        currentLevel.RunVisibilitySystem();

        return actionResult;
    }


    void ProcessTurnResults(ActionResult results)
    {
        foreach (var message in results.GetMessages()) { _log.AddMessage(message); }
        var deadEntities = results.GetEntityEvent("dead");
        if (deadEntities.Count() > 0)
        {
            var result = currentLevel.HandleDeadEntities(deadEntities);
            foreach (var message in result.GetMessages()) { _log.AddMessage(message); }

            if( result.TransitionToStateOnSuccess != GameState.Unspecified )
            {
                TransitionFrom(_gameState);
                TransitionTo(result.TransitionToStateOnSuccess);
            }
        }
    }

    private void ReportObjectsAtPosition(CellPosition pos)
    {
        var map = currentLevel.GetMapDTO();
        var entityNames = map.EntityMap.GetEntities().Where(e => e.position == pos).Select(e => e.GetColoredName());
        var backgroundNames = map.EntityFloorMap.GetEntities().Where(e => e.position == pos).Select(e => e.GetColoredName());

        var entitiesToLog = entityNames.Concat(backgroundNames);
        var message = "There is nothing there.";

        if (entitiesToLog.Count() > 0)
        {
            var names = string.Join(", ", entitiesToLog);
            message = $"You see: {names}";
        }

        _log.AddMessage(new Message(message, null));
    }

    void SetMoveDirection(Vector2Int direction)
    {
        var player = currentLevel.GetPlayer();
        CellPosition newPosition = new CellPosition(player.position.x + direction.x, player.position.y + direction.y);
        var action = new WalkAction(player.actor, new TargetData { targetPosition = newPosition });
        player.actor.SetNextAction(action);
    }

    void HandleUserInput()
    {
        if (_gameState == GameState.Global_LevelScene)
        {
            // Look Action
            if (Input.GetMouseButtonDown(0))
            {
                ReportObjectsAtPosition(MouseUtilities.GetCellPositionAtMousePosition(currentLevel.GetMapDTO().GroundMap));
            }

            HandleMovementKeys();

            if (Input.GetMouseButtonDown(1))
            {
                HandleContextSensitiveAction();
            }

            // Wait
            if (Input.GetKeyDown(KeyCode.Keypad5))
            {
                var player = currentLevel.GetPlayer();
                var action = new WaitAction(player.actor);
                player.actor.SetNextAction(action);
            }


            // MoveByPath
            if (Input.GetKeyDown(KeyCode.M) )
            {
                var player = currentLevel.GetPlayer();
                var action = new DeferAction(player.actor, new WalkAlongPathAction(player.actor));
                player.actor.SetNextAction(action);
            }

            // Pickup!
            if (Input.GetKeyDown(KeyCode.G))
            {
                var player = currentLevel.GetPlayer();
                var action = new PickupItemAction(player.actor);
                player.actor.SetNextAction(action);
            }

            // Inventory
            if (Input.GetKeyDown(KeyCode.I))
            {
                TransitionFrom(_gameState);
                TransitionTo(GameState.Global_InventoryMenu);

                // Open inventory of player
                _log.AddMessage(new Message($"{currentLevel.GetPlayer().actor.entity.GetColoredName()} opens their inventory...", null));
                var result = inventoryInterface.DescribeInventory();
                ProcessTurnResults(result);
            }

            // Back to main menu
            if( Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.BackQuote))
            {
                GameSceneManager.Instance.MainMenu();
            }

            if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.S))
            {
                SaveGameState();
            }
        }

        else if (_gameState == GameState.Global_InventoryMenu )
        {
            if (Input.GetKeyDown(KeyCode.I))
            {
                TransitionFrom(_gameState);
                TransitionTo(GameState.Global_LevelScene);
            }
            else
            {
                inventoryInterface.HandleItemKeyPress();
            }
        }
        else if ( _gameState == GameState.Global_ActionHandlerDeferred)
        {
            if( _deferredAction == null ) { _gameState = GameState.Global_LevelScene; return; }
            if(_deferredAction.UpdateHandler(currentLevel.GetMapDTO()))
            {
                var actor = currentLevel.GetActors().ElementAt(_currentActorId);
                actor.SetNextAction(_deferredAction);
                _deferredAction = null;
            }
        }

    }

    public void HandleContextSensitiveAction()
    {
        var currentDto = currentLevel.GetMapDTO();
        var cell = MouseUtilities.GetCellPositionAtMousePosition(currentLevel.GetMapDTO().GroundMap);
        var player = currentLevel.GetPlayer();

        var targetData = new TargetData { targetPosition = cell };

        // Cell Enemy? Attack
        if( currentDto.EntityMap.GetEntities(cell).Where(e => e != player).Any() )
        {
            player.actor.SetNextAction(new MeleeAttackAction(player.actor, targetData));
        }
        else if(cell == player.position)
        {
            if (currentDto.EntityFloorMap.GetEntities(cell).Where(e => e.gameObject.GetComponent<Item>() != null).Any()) {
                player.actor.SetNextAction(new PickupItemAction(player.actor));
            }
            else
            {
                player.actor.SetNextAction(new WaitAction(player.actor));
            }
        }
        else
        {
            player.actor.SetNextAction(new DeferAction(player.actor, new WalkAlongPathAction(player.actor)));
        }
    }

    public void TransitionTo(GameState gameState) {
        switch (gameState)
        {
            case GameState.Global_LevelScene:
                _gameState = gameState;
                break;
            case GameState.Global_InventoryMenu:
                _gameState = gameState;
                inventoryInterface.Show();
                break;
            case GameState.Global_ActionHandlerDeferred:
                _gameState = gameState;
                break;
            default:
                return;
        }
    }

    public void TransitionFrom(GameState gameState){
        if( gameState == GameState.Global_InventoryMenu ){
            inventoryInterface.Hide();
        }
    }

    void HandleMovementKeys()
    {
        Vector2Int direction = Vector2Int.zero;
        // Cardinals
        if (Input.GetKeyDown(KeyCode.Keypad4))
            direction.x = -1;
        if (Input.GetKeyDown(KeyCode.Keypad6))
            direction.x = 1;
        if (Input.GetKeyDown(KeyCode.Keypad2))
            direction.y = -1;
        if (Input.GetKeyDown(KeyCode.Keypad8))
            direction.y = 1;

        // Diagonals
        if (Input.GetKeyDown(KeyCode.Keypad7))
        {
            direction.x = -1; direction.y = 1;
        }
        if (Input.GetKeyDown(KeyCode.Keypad1))
        {
            direction.x = -1; direction.y = -1;
        }
        if (Input.GetKeyDown(KeyCode.Keypad3))
        {
            direction.x = 1; direction.y = -1;
        }
        if (Input.GetKeyDown(KeyCode.Keypad9))
        {
            direction.x = 1; direction.y = 1;
        }

        if (direction.x != 0 || direction.y != 0)
        {
            SetMoveDirection(direction);
        }
    }

    void SetDesiredScreenSize()
    {
        float aspect = (float)Screen.width / (float)Screen.height;
        var totalWidth = 1f + (float)(viewportWidth * 2f / cameraAdjustmentPercent);
        var screenSize = (float)totalWidth / 2f / aspect;

        Camera.main.orthographicSize = screenSize;
    }

    float CalculateCameraAdjustment()
    {
        var value = 0f;
        var totalWidth = (float)viewportWidth * 2f / cameraAdjustmentPercent;
        // var screenSize = totalWidth / 2 / aspect;
        value = totalWidth - (viewportWidth * 2f);

        calculatedCamerageAdjustment = value / 2;

        return calculatedCamerageAdjustment;
    }

    public void SaveGameState()
    {
        var location = Application.persistentDataPath + "/roguelike_save_data.json";
        Debug.Log(location);

        var saveData = new SaveData
        {
            currentActorId = _currentActorId,
            currentLevel = currentLevel.SaveGameState()
        };

        File.WriteAllText(location, Newtonsoft.Json.JsonConvert.SerializeObject(saveData, formatting: Newtonsoft.Json.Formatting.Indented));

        Debug.Log("Saved!");
    }

    private class SaveData
    {
        public int currentActorId;
        public Level.SaveData currentLevel;
    }
}

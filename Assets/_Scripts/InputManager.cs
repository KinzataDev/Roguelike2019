﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InputManager : MonoBehaviour
{
    void Start()
    {

    }

    void Update()
    {
        if (Input.anyKeyDown)
        {
            var gameEvent = EventManager.instance.GetGameEvent(EventManager.EventType.PlayerMove);
            Vector2Int direction = Vector2Int.zero;
            if (Input.GetKeyDown(KeyCode.Keypad4))
                direction.x = -1;
            if (Input.GetKeyDown(KeyCode.Keypad6))
                direction.x = 1;
            if (Input.GetKeyDown(KeyCode.Keypad2))
                direction.y = -1;
            if (Input.GetKeyDown(KeyCode.Keypad8))
                direction.y = 1;
            gameEvent.Raise(direction);
        }
    }
}

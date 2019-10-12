﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/**
The Entity is just that, an entity.

It has a position, sprite, color and knows how to update it's position
 */
public class Entity
{
    public Vector3Int position;
    public Sprite sprite;
    public Color color;
    public WorldTile tile;
    public bool blocks;
    public string name;
    public bool enemy;
    public Player playerComponent;
    public Fighter fighterComponent;
    public BasicMonsterAi aiComponent;

    public Entity((int x, int y) pos, SpriteType spriteType = SpriteType.Nothing, Color? color = null, bool blocks = false, string name = "mysterious enemy", bool enemy = false,
        Player player = null, Fighter fighter = null, BasicMonsterAi ai = null)
    {
        this.position = new Vector3Int(pos.x, pos.y, 0);
        this.sprite = SpriteLoader.instance.LoadSprite(spriteType);
        this.color = color ?? Color.magenta;
        this.blocks = blocks;
        this.name = name;
        this.enemy = enemy;
        this.playerComponent = player;
        this.fighterComponent = fighter;
        this.aiComponent = ai;

        tile = Tile.CreateInstance<WorldTile>();
        tile.sprite = this.sprite;
        tile.color = this.color;

        if (player != null)
        {
            player.owner = this;
        }

        if (fighter != null)
        {
            fighter.owner = this;
        }

        if (ai != null)
        {
            ai.owner = this;
        }
    }

    public Vector3Int Move(int dx, int dy)
    {
        position.x += dx;
        position.y += dy;

        return position;
    }

    public void MoveTorwards(int x, int y, EntityMap map, GroundMap groundMap)
    {
        var dx = x - position.x;
        var dy = y - position.y;
        var distance = (int)Mathf.Sqrt(dx * dx + dy * dy);

        dx = dx / distance;
        dy = dy / distance;

        var newX = position.x + dx;
        var newY = position.y + dy;

        if (!groundMap.IsBlocked(newX, newY) && map.GetBlockingEntityAtPosition(newX, newY) == null)
        {
            Move(dx, dy);
        }
    }

    public int DistanceTo(Entity other)
    {
        var dx = other.position.x - position.x;
        var dy = other.position.y - position.y;
        return (int)Mathf.Sqrt(dx * dx + dy * dy);
    }

    public ActionResult ConvertToDeadPlayer()
    {
        var actionResult = new ActionResult();
        sprite = SpriteLoader.instance.LoadSprite(SpriteType.Remains_Bones);
        color = new Color(.8f, .8f, .8f, 1);

        tile.sprite = sprite;
        tile.color = color;

        actionResult.AppendMessage(new Message("You died!", Color.red));
        return actionResult;
    }

    public ActionResult ConvertToDeadMonster()
    {
        var actionResult = new ActionResult();
        sprite = SpriteLoader.instance.LoadSprite(SpriteType.Remains_Skull);

        actionResult.AppendMessage(new Message($"{GetColoredName()} is dead!", null));

        color = new Color(.8f, .8f, .8f, 1);

        tile.sprite = sprite;
        tile.color = color;

        name = $"remains of {name.ToPronoun()}";
        blocks = false;
        fighterComponent = null;
        aiComponent = null;
        enemy = false;

        return actionResult;
    }

    public string GetColoredName()
    {
        return $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{name.ToPronoun()}</color>";
    }
}

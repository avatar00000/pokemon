using System;
using System.Linq;

public class RbyLedge {

    public byte Source;
    public byte Ledge;
    public Action ActionRequired;
}

public class RbyConnection {

    public RbyMap Map;
    public byte MapId;
    public ushort Source;
    public ushort Destination;
    public byte Length;
    public byte Width;
    public byte YAlignment;
    public byte XAlignment;
    public ushort Window;

    public RbyConnection(RbyMap map, ByteStream data) {
        Map = map;
        MapId = data.u8();
        Source = data.u16le();
        Destination = data.u16le();
        Length = data.u8();
        Width = data.u8();
        YAlignment = data.u8();
        XAlignment = data.u8();
        Window = data.u16le();
    }
}

public class RbyTile : Tile<RbyTile> {

    public RbyMap Map;

    public override RbyTile Right() {
        return Map[X + 1, Y];
    }

    public override RbyTile Left() {
        return Map[X - 1, Y];
    }

    public override RbyTile Up() {
        return Map[X, Y - 1];
    }

    public override RbyTile Down() {
        return Map[X, Y + 1];
    }

    public override bool IsPassable(PermissionSet permissions) {
        RbyWarp warp = Map.Warps[X, Y];
        if(warp != null && !warp.Allowed) return false;

        RbySprite sprite = Map.Sprites[X, Y];
        if(sprite != null && sprite.Movement != RbySpriteMovement.Walk) {
            return false;
        }

        return permissions.IsAllowed(Collision);
    }

    public override bool IsLedgeHop(RbyTile ledgeTile, Action action) {
        return ledgeTile != null && Map.Game.Ledges.Any(ledge => ledge.Source == Collision && ledge.Ledge == ledgeTile.Collision && ledge.ActionRequired == action);
    }

    public override int LedgeCost() {
        return 40;
    }
}

public class RbyWarp {

    public RbyMap Map;
    public byte Y;
    public byte X;
    public byte Index;
    public byte DestinationIndex;
    public byte DestinationMap;

    public bool Allowed;

    public RbyWarp(Rby game, RbyMap map, byte index, ByteStream data) {
        Map = map;
        Index = index;
        Y = data.u8();
        X = data.u8();
        DestinationIndex = data.u8();
        DestinationMap = data.u8();
        Allowed = false;
    }
}

public class RbyMap : Map<RbyTile> {

    public Rby Game;
    public byte Id;
    public byte Bank;
    public RbyTileset Tileset;
    public ushort DataPointer;
    public ushort TextPointer;
    public ushort ScriptPointer;
    public byte ConnectionFlags;
    public RbyConnection[] Connections;
    public ushort ObjectPointer;
    public byte BorderBlock;
    public DataList<RbyWarp> Warps;
    public DataList<RbySprite> Sprites;

    public RbyMap(Rby game, byte id, ByteStream data) {
        Game = game;
        Id = id;
        Bank = (byte) (data.Position >> 16);
        Tileset = game.Tilesets[data.u8()];
        Height = data.u8();
        Width = data.u8();
        DataPointer = data.u16le();
        TextPointer = data.u16le();
        ScriptPointer = data.u16le();
        ConnectionFlags = data.u8();
        Connections = new RbyConnection[4];
        for(int i = 3; i >= 0; i--) {
            if(((ConnectionFlags >> i) & 1) == 1) {
                Connections[i] = new RbyConnection(this, data);
            }
        }
        ObjectPointer = data.u16le();

        ByteStream objectData = game.ROM.From(Bank << 16 | ObjectPointer);

        BorderBlock = objectData.u8();

        Warps = new DataList<RbyWarp>();
        Warps.PositionCallback = obj => (obj.X, obj.Y);
        byte numWarps = objectData.u8();
        for(byte i = 0; i < numWarps; i++) {
            Warps.Add(new RbyWarp(game, this, i, objectData));
        }

        byte numSigns = objectData.u8();
        objectData.Seek(numSigns * 3); // TODO: Parse signs

        Sprites = new DataList<RbySprite>();
        Sprites.PositionCallback = obj => (obj.X, obj.Y);
        byte numSprites = objectData.u8();
        for(byte i = 0; i < numSprites; i++) {
            RbySprite sprite = new RbySprite(game, this, i, objectData);
            Sprites.Add(sprite);
            if(sprite.IsTrainer) {
                objectData.Seek(2);
            } else if(sprite.IsItem) {
                objectData.Seek(1);
            }
        }

        Tiles = new RbyTile[Width * 2, Height * 2];
        if(Tileset != null) {
            for(int i = 0; i < Width * Height; i++) {
                byte block = game.ROM[(Bank << 16 | DataPointer) + i];
                byte[] tiles = Game.ROM.Subarray(Tileset.Bank << 16 | Tileset.BlockPointer + block * 16, 16);
                for(int j = 0; j < 4; j++) {
                    int tileSpaceIndex = i * 2 + (j & 1) + (j >> 1) * (Width * 2) + (i / Width * 2 * Width);
                    byte xt = (byte) (tileSpaceIndex % (Width * 2));
                    byte yt = (byte) (tileSpaceIndex / (Width * 2));
                    Tiles[xt, yt] = new RbyTile {
                        Map = this,
                        X = xt,
                        Y = yt,
                        Collision = tiles[(j >> 1) * 8 + (j & 1) * 2 + 4],
                    };
                }
            }
        }
    }

    public override Bitmap Render() {
        byte[] tiles = Tileset.GetTiles(Game.ROM.Subarray(Bank << 16 | DataPointer, Width * Height), Width);

        byte[] pixels = new byte[tiles.Length * 16];
        for(int i = 0; i < tiles.Length; i++) {
            Array.Copy(Game.ROM.Data, Tileset.Bank << 16 | Tileset.GfxPointer + tiles[i] * 16, pixels, i * 16, 16);
        }

        Bitmap bitmap = new Bitmap(Width * 2 * 2 * 8, Height * 2 * 2 * 8);
        bitmap.Unpack2BPP(pixels);
        return bitmap;
    }
}
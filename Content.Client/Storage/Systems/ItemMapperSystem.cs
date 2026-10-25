using System.Linq;
using Content.Client.Clothing;
using Content.Shared.Clothing;
using Content.Shared.Item;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client.Storage.Systems;

public sealed partial class ItemMapperSystem : SharedItemMapperSystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ItemMapperComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ItemMapperComponent, AppearanceChangeEvent>(OnAppearance);
        SubscribeLocalEvent<ItemMapperComponent, GetEquipmentVisualsEvent>(OnGetEquipmentVisuals,
            after: [typeof(ClientClothingSystem)]);
    }

    private void OnStartup(EntityUid uid, ItemMapperComponent component, ComponentStartup args)
    {
        if (TryComp<SpriteComponent>(uid, out var sprite))
        {
            component.RSIPath ??= sprite.BaseRSI!.Path;
        }
    }

    private void OnAppearance(EntityUid uid, ItemMapperComponent component, ref AppearanceChangeEvent args)
    {
        if (TryComp<SpriteComponent>(uid, out var spriteComponent))
        {
            if (component.SpriteLayers.Count == 0)
            {
                InitLayers((uid, component, spriteComponent, args.Component));
            }

            EnableLayers((uid, component, spriteComponent, args.Component));
        }

        // Refresh wearer visuals only for ItemMappers that opt into clothing-state overlays.
        if (component.MapLayers.Values.Any(layer => layer.ClothingStates.Count > 0))
            _item.VisualsChanged(uid);
    }

    private void OnGetEquipmentVisuals(EntityUid uid, ItemMapperComponent component, GetEquipmentVisualsEvent args)
    {
        // Existing ItemMappers without clothing states (including utility belts) keep their original behavior.
        if (!component.MapLayers.Values.Any(layer => layer.ClothingStates.Count > 0)
            || !TryComp<AppearanceComponent>(uid, out var appearance)
            || !_appearance.TryGetData<ShowLayerData>(uid, StorageMapVisuals.LayerChanged, out var activeLayers, appearance)
            || !TryComp<SpriteComponent>(uid, out var sprite)
            || sprite.BaseRSI == null)
            return;

        foreach (var mapLayer in component.MapLayers.Values)
        {
            if (!activeLayers.QueuedEntities.Contains(mapLayer.Layer)
                || !mapLayer.ClothingStates.TryGetValue(args.Slot, out var state)
                || !sprite.BaseRSI.TryGetState(state, out _))
                continue;

            var key = $"{args.Slot}-{mapLayer.Layer}";
            args.Layers.Add((key, new PrototypeLayerData
            {
                RsiPath = sprite.BaseRSI.Path.ToString(),
                State = state,
                MapKeys = new HashSet<string> { key },
            }));
        }
    }

    private void InitLayers(Entity<ItemMapperComponent, SpriteComponent, AppearanceComponent> ent)
    {
        var (owner, component, spriteComponent, appearance) = ent;
        if (!_appearance.TryGetData<ShowLayerData>(owner, StorageMapVisuals.InitLayers, out var wrapper, appearance))
            return;

        component.SpriteLayers.AddRange(wrapper.QueuedEntities);

        foreach (var sprite in component.SpriteLayers)
        {
            _sprite.LayerMapReserve((owner, spriteComponent), sprite);
            _sprite.LayerSetSprite((owner, spriteComponent), sprite, new SpriteSpecifier.Rsi(component.RSIPath!.Value, sprite));
            _sprite.LayerSetVisible((owner, spriteComponent), sprite, false);
        }
    }

    private void EnableLayers(Entity<ItemMapperComponent, SpriteComponent, AppearanceComponent> ent)
    {
        var (owner, component, spriteComponent, appearance) = ent;
        if (!_appearance.TryGetData<ShowLayerData>(owner, StorageMapVisuals.LayerChanged, out var wrapper, appearance))
            return;

        foreach (var layerName in component.SpriteLayers)
        {
            var show = wrapper.QueuedEntities.Contains(layerName);
            _sprite.LayerSetVisible((owner, spriteComponent), layerName, show);
        }
    }
}

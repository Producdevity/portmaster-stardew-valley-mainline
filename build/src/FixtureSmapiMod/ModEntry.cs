using System;
using System.Collections;
using System.Collections.Generic;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace PortMaster.StardewValley.FixtureSmapiMod;

internal sealed class ModEntry : Mod
{
    private static readonly (string Id, string FilePath, bool StreamedVorbis)[] AudioFixtures =
    {
        ("PortMaster.Fixture.Wav", "PortMasterFixtureAudio/fixture.wav", false),
        ("PortMaster.Fixture.Ogg", "PortMasterFixtureAudio/fixture.ogg", false),
        ("PortMaster.Fixture.StreamedOgg", "PortMasterFixtureAudio/fixture-streamed.ogg", true),
    };

    public override void Entry(IModHelper helper)
    {
        helper.Events.Content.AssetRequested += OnAssetRequested;
        Monitor.Log("Fixture mod loaded", LogLevel.Info);
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo("Data/AudioChanges"))
            return;

        e.Edit(asset =>
        {
            var data = asset.GetData<object>();
            if (data is not IDictionary audioChanges)
                throw new InvalidOperationException("Data/AudioChanges did not load as a dictionary.");

            var dataArguments = data.GetType().GetGenericArguments();
            if (dataArguments.Length != 2)
                throw new InvalidOperationException("Data/AudioChanges dictionary type was not generic.");

            var cueDataType = dataArguments[1];
            foreach (var fixture in AudioFixtures)
            {
                audioChanges[fixture.Id] = CreateAudioCueData(
                    cueDataType,
                    fixture.Id,
                    fixture.FilePath,
                    fixture.StreamedVorbis);
            }

            Monitor.Log("Fixture injected Data/AudioChanges test entries", LogLevel.Info);
        });
    }

    private static object CreateAudioCueData(Type cueDataType, string id, string filePath, bool streamedVorbis)
    {
        var cueData = Activator.CreateInstance(cueDataType)
            ?? throw new InvalidOperationException($"Could not construct {cueDataType.FullName}.");

        SetField(cueDataType, cueData, "Id", id);
        SetField(cueDataType, cueData, "FilePaths", new List<string> { filePath });
        SetField(cueDataType, cueData, "Category", "Sound");
        SetField(cueDataType, cueData, "StreamedVorbis", streamedVorbis);
        SetField(cueDataType, cueData, "Looped", false);
        SetField(cueDataType, cueData, "UseReverb", false);

        return cueData;
    }

    private static void SetField(Type targetType, object instance, string name, object value)
    {
        var field = targetType.GetField(name)
            ?? throw new MissingFieldException(targetType.FullName, name);

        field.SetValue(instance, value);
    }
}

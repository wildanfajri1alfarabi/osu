// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.IO.Serialization;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Taiko;
using osu.Game.Skinning;
using osu.Game.Tests.Resources;
using osuTK;

namespace osu.Game.Tests.Beatmaps.Formats
{
    [TestFixture]
    public class LegacyBeatmapEncoderTest
    {
        private static readonly DllResourceStore beatmaps_resource_store = TestResources.GetStore();

        private static IEnumerable<string> allBeatmaps = beatmaps_resource_store.GetAvailableResources().Where(res => res.EndsWith(".osu", StringComparison.Ordinal));

        [TestCaseSource(nameof(allBeatmaps))]
        public void TestEncodeDecodeStability(string name)
        {
            var decoded = decodeFromLegacy(beatmaps_resource_store.GetStream(name), name);
            var decodedAfterEncode = decodeFromLegacy(encodeToLegacy(decoded), name);

            sort(decoded.beatmap);
            sort(decodedAfterEncode.beatmap);

            Assert.That(decodedAfterEncode.beatmap.Serialize(), Is.EqualTo(decoded.beatmap.Serialize()));
            Assert.IsTrue(areComboColoursEqual(decodedAfterEncode.beatmapSkin.Configuration, decoded.beatmapSkin.Configuration));
        }

        [Test]
        public void TestEncodeMultiSegmentSliderWithFloatingPointError()
        {
            var beatmap = new Beatmap
            {
                HitObjects =
                {
                    new Slider
                    {
                        Position = new Vector2(0.6f),
                        Path = new SliderPath(new[]
                        {
                            new PathControlPoint(Vector2.Zero, PathType.Bezier),
                            new PathControlPoint(new Vector2(0.5f)),
                            new PathControlPoint(new Vector2(0.51f)), // This is actually on the same position as the previous one in legacy beatmaps (truncated to int).
                            new PathControlPoint(new Vector2(1f), PathType.Bezier),
                            new PathControlPoint(new Vector2(2f))
                        })
                    },
                }
            };

            var decodedAfterEncode = decodeFromLegacy(encodeToLegacy((beatmap, new TestLegacySkin(beatmaps_resource_store, string.Empty))), string.Empty);
            var decodedSlider = (Slider)decodedAfterEncode.beatmap.HitObjects[0];
            Assert.That(decodedSlider.Path.ControlPoints.Count, Is.EqualTo(5));
        }

        private bool areComboColoursEqual(IHasComboColours a, IHasComboColours b)
        {
            // equal to null, no need to SequenceEqual
            if (a.ComboColours == null && b.ComboColours == null)
                return true;

            if (a.ComboColours == null || b.ComboColours == null)
                return false;

            return a.ComboColours.SequenceEqual(b.ComboColours);
        }

        private void sort(IBeatmap beatmap)
        {
            // Sort control points to ensure a sane ordering, as they may be parsed in different orders. This works because each group contains only uniquely-typed control points.
            foreach (var g in beatmap.ControlPointInfo.Groups)
            {
                ArrayList.Adapter((IList)g.ControlPoints).Sort(
                    Comparer<ControlPoint>.Create((c1, c2) => string.Compare(c1.GetType().ToString(), c2.GetType().ToString(), StringComparison.Ordinal)));
            }
        }

        private (IBeatmap beatmap, TestLegacySkin beatmapSkin) decodeFromLegacy(Stream stream, string name)
        {
            using (var reader = new LineBufferedReader(stream))
            {
                var beatmap = new LegacyBeatmapDecoder { ApplyOffsets = false }.Decode(reader);
                var beatmapSkin = new TestLegacySkin(beatmaps_resource_store, name);
                return (convert(beatmap), beatmapSkin);
            }
        }

        private class TestLegacySkin : LegacySkin
        {
            public TestLegacySkin(IResourceStore<byte[]> storage, string fileName)
                : base(new SkinInfo { Name = "Test Skin", Creator = "Craftplacer" }, storage, null, fileName)
            {
            }
        }

        private Stream encodeToLegacy((IBeatmap beatmap, ISkin beatmapSkin) fullBeatmap)
        {
            var (beatmap, beatmapSkin) = fullBeatmap;
            var stream = new MemoryStream();

            using (var writer = new StreamWriter(stream, Encoding.UTF8, 1024, true))
                new LegacyBeatmapEncoder(beatmap, beatmapSkin).Encode(writer);

            stream.Position = 0;

            return stream;
        }

        private IBeatmap convert(IBeatmap beatmap)
        {
            switch (beatmap.BeatmapInfo.RulesetID)
            {
                case 0:
                    beatmap.BeatmapInfo.Ruleset = new OsuRuleset().RulesetInfo;
                    break;

                case 1:
                    beatmap.BeatmapInfo.Ruleset = new TaikoRuleset().RulesetInfo;
                    break;

                case 2:
                    beatmap.BeatmapInfo.Ruleset = new CatchRuleset().RulesetInfo;
                    break;

                case 3:
                    beatmap.BeatmapInfo.Ruleset = new ManiaRuleset().RulesetInfo;
                    break;
            }

            return new TestWorkingBeatmap(beatmap).GetPlayableBeatmap(beatmap.BeatmapInfo.Ruleset);
        }

        private class TestWorkingBeatmap : WorkingBeatmap
        {
            private readonly IBeatmap beatmap;

            public TestWorkingBeatmap(IBeatmap beatmap)
                : base(beatmap.BeatmapInfo, null)
            {
                this.beatmap = beatmap;
            }

            protected override IBeatmap GetBeatmap() => beatmap;

            protected override Texture GetBackground() => throw new NotImplementedException();

            protected override Track GetBeatmapTrack() => throw new NotImplementedException();

            protected internal override ISkin GetSkin() => throw new NotImplementedException();

            public override Stream GetStream(string storagePath) => throw new NotImplementedException();
        }
    }
}

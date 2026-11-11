using NUnit.Framework;
using UnityEngine;

// RestraintCore.ParentBone と PlayerMovementLock.StrongestHold / HeldRatio の EditMode テスト。
// どちらも Udon を通さない純粋な static 関数なので、エディタの C# としてそのまま呼べる。
public class ParentBoneTests
{
    // TryGetBonePoseOrParent は bone 自身を含めて最大 12 個のボーンを試す。親をたどる回数は 11 回まで
    private const int MaxParentSteps = 11;

    private static int[] ChainToHips(HumanBodyBones bone, out bool reachedHips)
    {
        int[] chain = new int[MaxParentSteps + 1];
        HumanBodyBones current = bone;
        reachedHips = current == HumanBodyBones.Hips;
        int steps = 0;
        while (!reachedHips && steps < MaxParentSteps)
        {
            current = RestraintCore.ParentBone(current);
            chain[steps++] = (int)current;
            reachedHips = current == HumanBodyBones.Hips;
        }
        System.Array.Resize(ref chain, steps);
        return chain;
    }

    [Test]
    public void EveryBoneReachesHipsWithoutLoop()
    {
        for (int b = 0; b <= (int)HumanBodyBones.LastBone; b++)
        {
            HumanBodyBones bone = (HumanBodyBones)b;
            int[] chain = ChainToHips(bone, out bool reachedHips);
            Assert.IsTrue(reachedHips, $"{bone} が {MaxParentSteps} 回以内に Hips に着かない");
            for (int i = 0; i < chain.Length; i++)
            {
                Assert.AreNotEqual(b, chain[i], $"{bone} の鎖が自分に戻る");
                for (int j = i + 1; j < chain.Length; j++)
                {
                    Assert.AreNotEqual(chain[i], chain[j], $"{bone} の鎖が循環する");
                }
            }
        }
    }

    [Test]
    public void OutOfRangeValuesGoToHips()
    {
        Assert.AreEqual(HumanBodyBones.Hips, RestraintCore.ParentBone(HumanBodyBones.LastBone));
        Assert.AreEqual(HumanBodyBones.Hips, RestraintCore.ParentBone((HumanBodyBones)(-1)));
        Assert.AreEqual(HumanBodyBones.Hips, RestraintCore.ParentBone((HumanBodyBones)1000));
    }

    [Test]
    public void HipsIsItsOwnParent()
    {
        Assert.AreEqual(HumanBodyBones.Hips, RestraintCore.ParentBone(HumanBodyBones.Hips));
    }

    // 親が Hips になるのは Spine と左右の UpperLeg だけ。それ以外が Hips を返したら switch の書き漏れ
    [Test]
    public void OnlyDirectChildrenOfHipsReturnHips()
    {
        for (int b = 0; b < (int)HumanBodyBones.LastBone; b++)
        {
            HumanBodyBones bone = (HumanBodyBones)b;
            bool directChild = bone == HumanBodyBones.Hips || bone == HumanBodyBones.Spine
                || bone == HumanBodyBones.LeftUpperLeg || bone == HumanBodyBones.RightUpperLeg;
            if (directChild) continue;
            Assert.AreNotEqual(HumanBodyBones.Hips, RestraintCore.ParentBone(bone), $"{bone} の親が書かれていない");
        }
    }

    [TestCase(HumanBodyBones.Neck)]
    [TestCase(HumanBodyBones.LeftShoulder)]
    [TestCase(HumanBodyBones.RightShoulder)]
    public void UpperBodyChainPassesThroughTorso(HumanBodyBones bone)
    {
        int[] chain = ChainToHips(bone, out bool _);
        CollectionAssert.AreEqual(
            new[] { (int)HumanBodyBones.UpperChest, (int)HumanBodyBones.Chest, (int)HumanBodyBones.Spine, (int)HumanBodyBones.Hips },
            chain);
    }

    [Test]
    public void FingerTipChainFollowsArm()
    {
        int[] chain = ChainToHips(HumanBodyBones.LeftIndexDistal, out bool _);
        CollectionAssert.AreEqual(
            new[]
            {
                (int)HumanBodyBones.LeftIndexIntermediate, (int)HumanBodyBones.LeftIndexProximal, (int)HumanBodyBones.LeftHand,
                (int)HumanBodyBones.LeftLowerArm, (int)HumanBodyBones.LeftUpperArm, (int)HumanBodyBones.LeftShoulder,
                (int)HumanBodyBones.UpperChest, (int)HumanBodyBones.Chest, (int)HumanBodyBones.Spine, (int)HumanBodyBones.Hips,
            },
            chain);
    }
}

public class MovementLockHeldTests
{
    private const float Free = PlayerMovementLock.Free;

    [Test]
    public void EmptyIsOne()
    {
        Assert.AreEqual(1f, PlayerMovementLock.HeldRatio(new float[PlayerMovementLock.MaxHolders], 0));
        Assert.AreEqual(1f, PlayerMovementLock.HeldRatio(null, 0));
        Assert.AreEqual(Free, PlayerMovementLock.StrongestHold(new float[PlayerMovementLock.MaxHolders], 0));
    }

    [Test]
    public void FreeOnlyIsOne()
    {
        float[] values = { Free, Free, Free };
        Assert.AreEqual(1f, PlayerMovementLock.HeldRatio(values, values.Length));
        Assert.AreEqual(Free, PlayerMovementLock.StrongestHold(values, values.Length));
    }

    [Test]
    public void FreeMixedTakesMinimumOfValues()
    {
        float[] values = { Free, .5f, Free, .3f };
        Assert.AreEqual(.3f, PlayerMovementLock.HeldRatio(values, values.Length));
    }

    [Test]
    public void SeveralValuesTakeMinimum()
    {
        float[] values = { .8f, .2f, .5f };
        Assert.AreEqual(.2f, PlayerMovementLock.HeldRatio(values, values.Length));
    }

    [Test]
    public void ZeroIsAHoldNotFree()
    {
        float[] values = { .5f, 0f };
        Assert.AreEqual(0f, PlayerMovementLock.HeldRatio(values, values.Length));
    }

    // Apply と同じく、負の値はどれも Free として扱う
    [Test]
    public void AnyNegativeIsFree()
    {
        float[] values = { -.5f, .7f };
        Assert.AreEqual(.7f, PlayerMovementLock.HeldRatio(values, values.Length));
    }

    // 配列は MaxHolders 件あり、count より後ろは見ない
    [Test]
    public void IgnoresEntriesAfterCount()
    {
        float[] values = { .5f, .1f };
        Assert.AreEqual(.5f, PlayerMovementLock.HeldRatio(values, 1));
    }
}

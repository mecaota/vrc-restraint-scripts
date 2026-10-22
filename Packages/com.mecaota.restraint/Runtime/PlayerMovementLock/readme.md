# PlayerMovementLock

## 概要

ローカルプレイヤーの移動速度(歩く/走る/横移動)・ジャンプ・重力を、複数のギミックから重ねて絞るための共有部品です。1 ワールドに 1 つだけ置きます。

各ギミックは自分を holder として `Hold` / `Release` を呼ぶだけです。元の値の保存と復元はこの部品が受け持ちます。
以前のように各ギミックが自前で保存・復元すると、別のギミックが既に絞った値を「元の値」として保存してしまい、解除後も減速が残ることがありました(例: 木の巣の 1/3 と落下網の 1/10 が重なると、解除の順序しだいで 1/3 のまま)。

同期はしません(`BehaviourSyncMode.None`)。速度・ジャンプ・重力の設定はローカル専用なので、各クライアントが自分の値だけを扱います。

## 置き方

- シーンに `PlayerMovementLock.prefab` を置きます。オブジェクト名は `PlayerMovementLock` のままにしてください。
- ギミックの Prefab からシーンを参照したくない場合は、ギミック側に Prefab への参照(`GameObject movementLockPrefab`)を持たせ、`PlayerMovementLock.Resolve(cached, prefab)` で取得します。名前でシーンから探し、無ければ Prefab から作ります。

```csharp
public GameObject movementLockPrefab;
private PlayerMovementLock movementLock;

private PlayerMovementLock Lock()
{
    movementLock = PlayerMovementLock.Resolve(movementLock, movementLockPrefab);
    return movementLock;
}

// 捕まえた: 移動を 1/10、ジャンプを 0 に。重力はそのまま
if (Lock() != null) movementLock.Hold(this, .1f, 0f, PlayerMovementLock.Free);
// 放した
if (Lock() != null) movementLock.Release(this);
```

複数のギミックが同じシーンで `Resolve` を呼んでも、作られるのは 1 つだけです(名前で見つかるため)。
ギミックの `Start` で一度 `Lock()` を呼んでおくと、Prefab を配線していない別のギミックもそのロックを名前で見つけられます。

## API

- `Hold(holder, move, jump, gravity)`: holder がローカルプレイヤーを絞ります。
  - `move`: 歩く/走る/横移動を元の何倍にするか
  - `jump`: ジャンプを元の何倍にするか
  - `gravity`: 重力の値そのもの(倍率ではありません)
  - `PlayerMovementLock.Free`(-1)を渡した項目には触りません
  - 同じ値で毎フレーム呼んでも構いません。別のシステムに上書きされていたら掛け直します
- `Release(holder)`: holder の制限を外します。最後の 1 つなら元の値に戻します。ただし、この部品が入れた値のままの項目だけです(別のシステムが途中で変えた値は残します)
- `ReleaseAndRestore(holder)`: 上と同じですが、最後の 1 つなら無条件で元の値に戻します(空中で捕まえた相手を地上で引き渡すときなど)
- `Holds(holder)`: holder が制限を掛けているか
- `BaseWalk()` / `BaseRun()`: 制限を掛ける前のプレイヤー本来の値(誰も掛けていなければ今の値)
- `BaseGravity()`: 元の重力。ほぼ 0(飛行中に捕まった)なら `fallbackGravity`

## 動き

- 最初の `Hold` で、その時点の歩く/走る/横移動/ジャンプ/重力を「元の値」として保存します
- 複数の holder がいるときは、項目ごとに一番強い制限(最小値)を使います。掛けた順に依存しません
- 最後の holder が外れたら元の値に戻します。元の重力がほぼ 0 だった場合は `fallbackGravity` に戻します(飛行中に捕まった人が解除後も浮いたままにならないように)
- ローカルプレイヤーがリスポーンしたら、すべての制限を外して元に戻します
- holder は最大 16 件(`MaxHolders`)

## 設定項目

- **Fallback Gravity**: 元の重力がほぼ 0(飛行中など)だったときに、解除で戻す重力(既定 1)

## PlayerSpeedLimiter(イベントで掛ける減速)

`PlayerMovementLock` をスクリプトを書かずに使うための部品です。同じフォルダの `PlayerSpeedLimiter.cs` を付けた GameObject を置き、
ほかのギミックから `SendCustomEvent` で呼びます。元の速度の保存と復元は `PlayerMovementLock` が受け持つので、
別のギミックの減速と重なっても、解除の順番に関係なく正しく戻ります。

- `Engage`: 掛けます。何か所から呼ばれても回数を数え、`Engage` が `Disengage` より多い間だけ掛けます
- `Disengage`: `Engage` を 1 回分外します。全部外れたら元の速度に戻します
- 速度はローカル専用なので同期しません。送り手は「自分が」捕まった / 放されたときだけ、自分のクライアントで送ってください

送り手はこの部品の型を知らず、イベント名だけで呼ぶので、送り手のパッケージは restraint が無くてもコンパイルできます。

### 蜘蛛の巣(spider-shader の SurfaceContactDeformer)とつなぐ

1. シーンに `PlayerSpeedLimiter` を 1 つ置き、`Move Ratio` を決めます(例: 1/3 なら 0.333)
2. 各巣の `Local Stuck Receivers` にその GameObject を入れます。イベント名は既定の `Engage` / `Disengage` のままで構いません
3. 巣ごとに倍率を変えたいときは、倍率ごとに `PlayerSpeedLimiter` を分けて置きます(複数の巣に同時にくっついたときは、一番遅い倍率が使われます)

### 拘束中に掛ける(RestraintCore とつなぐ)

`Core` に `RestraintCore` を入れ、その Core の `Receivers` にこの部品を入れると、Core に装着されているのが自分の間も掛けます(他人の装着では掛けません)。
`Move Ratio` を 0 にすれば、拘束されている間は歩けなくなります。Core と同じ GameObject には置かないでください(Core は Manual 同期、この部品は同期なしのため)。

### 設定項目

- **Move Ratio**: 掛けている間、歩く / 走る / 横移動を元の何倍にするか(0〜1。既定 0.5)
- **Jump Ratio**: 掛けている間、ジャンプを元の何倍にするか。負の値(既定 -1)ならジャンプには触りません
- **Movement Lock Prefab**: シーンに `PlayerMovementLock` が無いときに作る Prefab。シーンに置いてあれば空で構いません
- **Core / Channel**: 上の「拘束中に掛ける」用。空なら Core には反応しません

### 注意

- GameObject は有効のままにしてください。無効の間に届いたイベントは捨てられるので、無効にされた時点で外して回数を 0 に戻します
- ローカルプレイヤーがリスポーンしたら、`Engage` の回数を 0 に戻します(`PlayerMovementLock` もリスポーンで全部外すため)

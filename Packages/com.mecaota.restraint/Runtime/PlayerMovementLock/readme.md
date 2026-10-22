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

## PlayerSpeedLimiter(イベントで速度を設定・増減・リセットする)

ローカルプレイヤーの移動速度(歩く / 走る / 横移動)を、`SendCustomEvent` で操作するための部品です。同じフォルダの `PlayerSpeedLimiter.cs` を付けた GameObject を置き、
ほかのギミック(ボタン、トリガーなど)からイベント名で呼びます。元の速度の保存と復元は `PlayerMovementLock` が受け持つので、
別のギミックの減速と重なっても、解除の順番に関係なく正しく戻ります。

速度は「元の速度の何倍か」(倍率)で扱います。1 = 元の速度、0.5 = 半分、0 = 移動できない、です。

| イベント | 内容 |
|---|---|
| `SetSpeed` | 倍率を `Set Ratio` にする |
| `AddSpeed` | 倍率を `Step Ratio` だけ増やす |
| `SubtractSpeed` | 倍率を `Step Ratio` だけ減らす |
| `ResetSpeed` | 倍率を `Default Ratio`(元の速度)に戻す |

- どれも引数なしです。値はインスペクタで決めておきます。スクリプトから別の値で呼びたいときは、先に `setRatio` などのフィールドへ書いてから呼びます
- 掛ける倍率は `Min Ratio`〜`Max Ratio` に収めます。ただし、増減で範囲の外に出た分も覚えています。そのため `SubtractSpeed` と `AddSpeed` を対で呼べば、何回重なっても必ず元に戻ります
- 倍率が 1 のときは制限を外します。ほかのギミックの制限と重なったときは、一番遅いものが使われます
- `GetRatio()` で、いま掛けている倍率(範囲に収めた後の値)を読めます
- 速度はローカル専用なので同期しません。送り手は自分のクライアントで、自分に関係するときだけ送ってください
- 送り手はこの部品の型を知らず、イベント名だけで呼ぶので、送り手のパッケージは restraint が無くてもコンパイルできます

### 使い方の例

- **ボタンで速さを変える**: 「速く」ボタンから `AddSpeed`、「遅く」ボタンから `SubtractSpeed`、「戻す」ボタンから `ResetSpeed`。`Max Ratio` を 1 より大きくすれば、元より速くもできます
- **入っている間だけ遅くなる床**: トリガーに入ったら `SetSpeed`、出たら `ResetSpeed`
- **重なることがある減速エリア**: 各エリアに入ったら `SubtractSpeed`、出たら `AddSpeed`。何か所から送っても 1 つで済みます
  - いくつ重なっても 1/3 にしたい(重ねて遅くしない)ときは、`Step Ratio` 0.6667・`Min Ratio` 0.3333
  - 重なるほど遅くしたいときは、`Step Ratio` を小さく(例: 0.3 なら 1 か所で 0.7、2 か所で 0.4)
  - 場所ごとに遅さを変えたいときは、遅さごとに `PlayerSpeedLimiter` を分けます
- **拘束中は歩けなくする**: 下の「RestraintCore とつなぐ」で `Set Ratio` を 0 に

### RestraintCore とつなぐ

`Core` に `RestraintCore` を入れ、その Core の `Receivers` にこの部品を入れると、Core に自分が装着されたら `SetSpeed`、外れたら `ResetSpeed` します(他人の装着では動きません)。
Core と同じ GameObject には置かないでください(Core は Manual 同期、この部品は同期なしのため)。
`SubtractSpeed` / `AddSpeed` を対で送る送り手と同じ部品を共有すると、Core が外れたときの `ResetSpeed` でその分も消えるので、送り手ごとに分けて置いてください。

### 設定項目

- **Default Ratio**: 元の速度(既定 1)。`ResetSpeed` でこの倍率に戻します。最初もこの倍率から始まります
- **Set Ratio**: `SetSpeed` でこの倍率にします(既定 0.5)
- **Step Ratio**: `AddSpeed` / `SubtractSpeed` で増やす・減らす量(既定 0.25)
- **Min Ratio / Max Ratio**: 掛ける倍率の範囲(既定 0〜1)
- **Movement Lock Prefab**: シーンに `PlayerMovementLock` が無いときに作る Prefab。シーンに置いてあれば空で構いません
- **Core / Channel**: 上の「RestraintCore とつなぐ」用。空なら Core には反応しません

### 注意

- GameObject は有効のままにしてください。無効の間に届いたイベントは捨てられます(無効にした時点で制限を外し、有効に戻したら今の倍率で掛け直します)
- リスポーンでは倍率を変えません。`PlayerMovementLock` がリスポーンで外した分は、次のフレームで掛け直します。リスポーンで戻したいときは、送り手から `ResetSpeed` を呼んでください

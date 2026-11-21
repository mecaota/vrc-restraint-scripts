# PlayerEncumbranceController

## 概要

ローカルプレイヤーの移動速度(歩く/走る/横移動)・ジャンプ・重力を、複数のギミックから重ねて絞るための部品です。プレイヤーごとの動きにくさ(0〜255)も持ちます。
置いた場所によって、役割が 2 つに分かれます。

- **まとめ役**: ローカルプレイヤーの PlayerObject の複製。元の値の保存と復元と、動きにくさを受け持ちます
- **窓口**(それ以外の場所に置いたもの): 速度のイベント(`SetSpeed` など)を受け、まとめ役に制限を頼みます

各ギミックは自分を holder として `Hold` / `Release` を呼ぶだけです。元の値の保存と復元はまとめ役が受け持ちます。
以前のように各ギミックが自前で保存・復元すると、別のギミックが既に絞った値を「元の値」として保存してしまい、解除後も減速が残ることがありました(例: 1/3 にする減速と 1/10 にする減速が重なると、解除の順序しだいで 1/3 のまま)。

スクリプトを書かずに使うときは、窓口を置いて `SendCustomEvent` で速度を変えます(下の「速度のイベント」)。

同期は Manual です。同期するのは動きにくさだけで、速度・ジャンプ・重力の設定はローカル専用です(各クライアントが自分の値だけを扱います)。

## 置き方

- まとめ役: PlayerObject(`VRCPlayerObject`)のテンプレートの**根**に、この部品を付けます。各プレイヤーに複製が作られ、ローカルプレイヤーの複製がまとめ役になります。テンプレートの根には 1 つだけ置いてください
- ギミックからは `PlayerEncumbranceController.Resolve(cached)` で取ります。ローカルプレイヤーの PlayerObject から探すので、シーンへの参照も Prefab も要りません。入室直後でまだ複製が無いときは null を返します
- 窓口: 好きな GameObject に `PlayerEncumbranceController` を付けます。まとめ役が見つかるまでは、1 秒ごとに掛け直します

```csharp
private PlayerEncumbranceController controller;

private PlayerEncumbranceController Controller()
{
    controller = PlayerEncumbranceController.Resolve(controller);
    return controller;
}

// 捕まえた: 移動を 1/10、ジャンプを 0 に。重力はそのまま
if (Controller() != null) controller.Hold(this, .1f, 0f, PlayerEncumbranceController.Free);
// 放した
if (Controller() != null) controller.Release(this);
```

## API

- `Hold(holder, move, jump, gravity)`: holder がローカルプレイヤーを絞ります。
  - `move`: 歩く/走る/横移動を元の何倍にするか
  - `jump`: ジャンプを元の何倍にするか
  - `gravity`: 重力の値そのもの(倍率ではありません)
  - `PlayerEncumbranceController.Free`(-1)を渡した項目には触りません
  - 同じ値で毎フレーム呼んでも構いません。別のシステムに上書きされていたら掛け直します
- `Release(holder)`: holder の制限を外します。最後の 1 つなら元の値に戻します。ただし、まとめ役が入れた値のままの項目だけです(別のシステムが途中で変えた値は残します)
- `ReleaseAndRestore(holder)`: 上と同じですが、最後の 1 つなら無条件で元の値に戻します(移動の制御をほかの仕組みに引き渡すときなど)
- `Holds(holder)`: holder が制限を掛けているか
- `BaseWalk()` / `BaseRun()`: 制限を掛ける前のプレイヤー本来の値(誰も掛けていなければ今の値)
- `BaseGravity()`: 元の重力。ほぼ 0(飛行中に掛けられた)なら `fallbackGravity`
- `HeldMove()` / `HeldJump()`: いま頼まれている移動・ジャンプの倍率です。掛かっている `Hold` の `move` / `jump` から `Free` を除いた最小値を返します。誰も掛けていないときと、全員が `Free` のときは 1 です
  - 返すのは頼まれた倍率です。別のシステムが速度を上書きしていても、その実際の値は返しません
  - 重力は扱いません(`Hold` の `gravity` は倍率ではなく、値そのもののため)
- `_Add(int)`: 動きにくさを増減します(負なら減らす)。本人のまとめ役で、有効なときだけ効きます。値が変わらなくても掛け直すので、`immobileAt` を変えた後は `_Add(0)` を呼びます
- `_Reset()`: 動きにくさを 0 に戻します(`SendCustomEvent` 可)

窓口に対して `Hold` などを呼んでも、そのまままとめ役に渡します。`_Add` / `_Reset` は、まとめ役(PlayerObject の複製)に対して呼んでください。

## 動き

- 最初の `Hold` で、その時点の歩く/走る/横移動/ジャンプ/重力を「元の値」として保存します
- 複数の holder がいるときは、項目ごとに一番強い制限(最小値)を使います。掛けた順に依存しません
- 最後の holder が外れたら元の値に戻します。元の重力がほぼ 0 だった場合は `fallbackGravity` に戻します(飛行中に掛けられた人が解除後も浮いたままにならないように)
- ローカルプレイヤーがリスポーンしたら、すべての制限を外して元に戻し、動きにくさを 0 にします
- holder は最大 16 件(`MaxHolders`)

## 動きにくさ

まとめ役は、プレイヤーごとの動きにくさ(0〜255)を持ちます。値と `Immobile At` は本人の端末だけが書き、Manual 同期で全端末へ送ります(0.2 秒分まとめて送ります)。`Immobile At` をスクリプトから変えたら `_Add(0)` を呼んでください。

- 値が `Immobile At` より小さい間は、移動を「1 − 値 ÷ `Immobile At`」倍以下にします(まとめ役自身の倍率と小さい方)。`Immobile At` 以上で、移動もジャンプもできません
- もがくと、1 回ごとに `Recover Amount` だけ減ります。VR は手を速く振り返した時、デスクトップは移動キーを新しく倒した時に数えます
- 時間では減りません。リスポーンと `_Reset()` で 0 に戻ります
- `Animator` を設定すると、全端末で値が変わるたびに float の `Amount` に 値 ÷ 255 を、bool の `Immobile` に「値 ≥ `Immobile At`」を入れます。どちらのパラメーターも Animator に用意してください(無いと、値が変わるたびに全端末で警告が出ます)
- ほかのギミックから値を読むときは、同期変数 `amount`(int)を `GetProgramVariable("amount")` で読みます

## 速度のイベント

窓口(またはまとめ役)に `SendCustomEvent` で送ると、ローカルプレイヤーの移動速度(歩く / 走る / 横移動)を変えます。
ほかのギミック(ボタン、トリガーなど)からイベント名で呼びます。窓口は自分を holder としてまとめ役に `Hold` するので、
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
- `GetRatio()` で、いま掛けている倍率(範囲に収めた後の値。動きにくさは含みません)を読めます
- 速度はローカル専用です。送り手は自分のクライアントで、自分に関係するときだけ送ってください
- 送り手はこの部品の型を知らなくても、イベント名だけで呼べます(送り手のパッケージは restraint が無くてもコンパイルできます)

### 使い方の例

- **ボタンで速さを変える**: 「速く」ボタンから `AddSpeed`、「遅く」ボタンから `SubtractSpeed`、「戻す」ボタンから `ResetSpeed`。`Max Ratio` を 1 より大きくすれば、元より速くもできます
- **入っている間だけ遅くなる床**: トリガーに入ったら `SetSpeed`、出たら `ResetSpeed`
- **重なることがある減速エリア**: 各エリアに入ったら `SubtractSpeed`、出たら `AddSpeed`。何か所から送っても、窓口は 1 つで済みます
  - いくつ重なっても 1/3 にしたい(重ねて遅くしない)ときは、`Step Ratio` 0.6667・`Min Ratio` 0.3333
  - 重なるほど遅くしたいときは、`Step Ratio` を小さく(例: 0.3 なら 1 か所で 0.7、2 か所で 0.4)
  - 場所ごとに遅さを変えたいときは、遅さごとに窓口を分けます
- **拘束中は歩けなくする**: 下の「RestraintCore とつなぐ」で `Set Ratio` を 0 に

### RestraintCore とつなぐ

窓口の `Core` に `RestraintCore` を入れ、その Core の `Receivers` に窓口を入れると、Core に自分が装着されたら `SetSpeed`、外れたら `ResetSpeed` します(他人の装着では動きません)。
Core の `Network Event Target` は `Self` のままにしてください。
`SubtractSpeed` / `AddSpeed` を対で送る送り手と同じ窓口を共有すると、Core が外れたときの `ResetSpeed` でその分も消えるので、送り手ごとに窓口を分けて置いてください。

## 設定項目

### まとめ役の設定(窓口では使わない)

- **Fallback Gravity**: 元の重力がほぼ 0(飛行中など)だったときに、解除で戻す重力(既定 1)

### 速度の倍率

- **Default Ratio**: 元の速度(既定 1)。`ResetSpeed` でこの倍率に戻します。最初もこの倍率から始まります
- **Set Ratio**: `SetSpeed` でこの倍率にします(既定 0.5)
- **Step Ratio**: `AddSpeed` / `SubtractSpeed` で増やす・減らす量(既定 0.25)
- **Min Ratio / Max Ratio**: 掛ける倍率の範囲(既定 0〜1)

### RestraintCore(任意)

- **Core / Channel**: 上の「RestraintCore とつなぐ」用。空なら Core には反応しません

### 動きにくさ(まとめ役だけが使う)

- **Immobile At**: この値以上で移動もジャンプもできない(既定 32。1 以上に丸めます)
- **Recover Amount**: もがき 1 回で減らす量(既定 2)
- **Animator**: 値 ÷ 255 を float の `Amount` に、値 ≥ `Immobile At` を bool の `Immobile` に入れる Animator。空なら何もしません

## 注意

- GameObject とこの部品は有効のままにしてください。どちらかが無効の間は制限を掛けません(無効にした時点で自分の分の制限を外し、有効に戻したら今の倍率と値で掛け直します)。無効の間に変えた倍率は覚えておき、有効に戻ったときに掛けます。動きにくさは無効の間は変わりません
- リスポーンでは倍率を変えません。まとめ役がリスポーンで外した分は、次のフレームで掛け直します。リスポーンで戻したいときは、送り手から `ResetSpeed` を呼んでください
- PlayerObject のテンプレートの倍率(`Default Ratio`)は 1 のまま、`Core` は空のままにしてください。テンプレートの設定は全員の複製に入り、ほかの人の複製も窓口として動くので、自分の速度を絞ってしまいます

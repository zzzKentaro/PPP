# PPP - Poppy Projection Playground -

PPP（Poppy Projection Playground）は，付箋などの実物体を用いて，投影映像内の物理挙動を変化させるインタラクションシステムです。

本システムでは，Unity上で動作する2D物理シミュレーション映像をプロジェクターやモニターに表示し，その表示面をWebカメラで撮影します。表示面に貼られたピンク色の付箋をPython/OpenCVで検出し，その位置・大きさ・角度をUnityへUDP通信で送信します。Unity側では，受信した情報をもとに仮想的な障害物を生成し，落下するボールの衝突挙動に反映します。

これにより，体験者は画面上のボールを直接操作するのではなく，現実空間に付箋を貼る・動かす・剥がすという行為によって，映像内の物理シミュレーションに介入できます。

## Demo

<!-- デモ画像やGIFがある場合は，以下のように配置してください。 -->

<!--
![Demo](./docs/images/demo.gif)
-->

## Concept

PPPの中心となる体験は，現実空間に配置した実物体が，仮想空間内の物理挙動に影響を与えることです。

通常，デジタル上の物理シミュレーションは，マウス，キーボード，ゲームコントローラー，タッチパネルなどを用いて操作します。一方でPPPでは，付箋のような身近な実物体を操作手段として用います。

たとえば，投影映像内でボールが落下しているとき，体験者が表示面に付箋を貼ると，Unity内にその付箋と対応する障害物が生成されます。ボールはその障害物に衝突し，跳ね返ったり，進行方向を変えたりします。

このように，現実空間での「貼る」「動かす」「剥がす」という行為を通して，映像内の物理挙動を変化させることができます。

## Features

* 付箋などの実物体を用いた直感的なインタラクション
* Webカメラによる投影面・表示面の撮影
* 4点クリックによる投影領域のキャリブレーション
* OpenCVによるピンク色の付箋検出
* 検出した付箋の位置・大きさ・角度の取得
* UDP/JSONによるPythonからUnityへのリアルタイム通信
* Unity 2D物理エンジンによるボールと障害物の衝突表現
* プロジェクターだけでなく，モニター表示でも動作可能

## System Overview

PPPは，主に以下の2つのプログラムで構成されています。

```text
Python / OpenCV
  ├─ Webカメラ映像の取得
  ├─ 投影領域のキャリブレーション
  ├─ 画像の射影変換
  ├─ ピンク色の付箋検出
  └─ UDPでUnityへJSON送信

Unity 2D
  ├─ UDPでJSON受信
  ├─ 付箋情報をUnity座標へ変換
  ├─ 障害物オブジェクトを生成・更新
  ├─ ボールを一定間隔で生成
  └─ 2D物理演算で衝突挙動を表現
```

## How It Works

### 1. Projection Area Calibration

Python側では，Webカメラで撮影した映像上で，投影映像またはモニター表示領域の四隅をクリックします。

クリック順は以下の通りです。

```text
1. 左上
2. 右上
3. 右下
4. 左下
```

この4点をもとに，OpenCVの射影変換を用いて，カメラ画像内の表示領域を `1280 x 720` の投影空間へ変換します。

キャリブレーション結果は `ppp_calibration.json` に保存できるため，同じカメラ・表示環境であれば次回以降も再利用できます。

### 2. Sticky Note Detection

射影変換後の画像に対して，HSV色空間を用いた色検出を行います。

現在のプロトタイプでは，ピンク色の付箋を検出対象としています。

```python
LOWER_HSV = np.array([140, 60, 60], dtype=np.uint8)
UPPER_HSV = np.array([179, 255, 255], dtype=np.uint8)
```

検出処理では，以下の手順を行っています。

1. BGR画像をHSV画像へ変換
2. 指定したHSV範囲でマスク画像を作成
3. モルフォロジー処理でノイズを除去
4. 輪郭を抽出
5. 一定面積以上の輪郭を付箋候補として扱う
6. `cv2.minAreaRect` により，矩形の中心座標・幅・高さ・角度を取得する

取得した矩形情報は，画像サイズに対する正規化座標として扱います。

```json
{
  "id": 0,
  "x": 0.50,
  "y": 0.40,
  "w": 0.12,
  "h": 0.06,
  "angle": 30.0
}
```

`x`, `y`, `w`, `h` は，投影空間に対する `0.0` から `1.0` の正規化値です。

### 3. UDP Communication

Python側は，検出した付箋情報をJSON形式に変換し，UDPでUnityへ送信します。

デフォルト設定は以下の通りです。

```text
Host: 127.0.0.1
Port: 5005
Send rate: 30 FPS
```

送信されるJSONの形式は以下の通りです。

```json
{
  "frame_w": 1280,
  "frame_h": 720,
  "obstacles": [
    {
      "id": 0,
      "x": 0.50,
      "y": 0.40,
      "w": 0.12,
      "h": 0.06,
      "angle": 30.0
    }
  ]
}
```

### 4. Unity Obstacle Generation

Unity側では，`PPPUdpReceiver.cs` がUDP通信を受信します。

受信したJSONはUnityのメインスレッド上で解析され，`PPPObstacleManager.cs` に渡されます。

`PPPObstacleManager` は，受信した付箋情報に対応する障害物オブジェクトを生成・更新します。各障害物には以下のコンポーネントが付与されます。

* `Rigidbody2D`
* `BoxCollider2D`
* `SpriteRenderer`，デバッグ表示用

付箋の座標は，Python側では画面左上を原点とする正規化座標ですが，Unity側では中央を原点とするワールド座標へ変換されます。

```csharp
float worldX = (x - 0.5f) * simulationWorldSize.x;
float worldY = (0.5f - y) * simulationWorldSize.y;
```

デフォルトでは，Unity内のシミュレーション領域は以下のサイズとして扱われます。

```text
simulationWorldSize = 16 x 9
```

そのため，16:9の投影映像とUnity内の物理空間を対応させやすくなっています。

## Unity Scripts

### `PPPUdpReceiver.cs`

Pythonから送信されたUDPパケットを受信するスクリプトです。

バックグラウンドスレッドでUDP受信を行い，受信した最新JSONをUnityの `Update()` 内で処理します。これにより，Unityのメインスレッドを止めずに外部プログラムからのデータを受け取ることができます。

主な役割は以下の通りです。

* UDPポート `5005` で待ち受ける
* Pythonから送られたJSON文字列を受信する
* JSONを `ObstacleFrameDto` として解析する
* `PPPObstacleManager` に障害物情報を渡す

### `PPPObstacleManager.cs`

受信した付箋情報をもとに，Unity内の障害物を生成・更新・削除するスクリプトです。

主な役割は以下の通りです。

* 正規化座標をUnityワールド座標へ変換する
* 付箋に対応する `BoxCollider2D` を生成する
* 位置・大きさ・角度を更新する
* 一定時間検出されなくなった障害物を削除する
* デバッグ用の半透明スプライトを表示する

### `PPPBallSpawner.cs`

一定間隔でボールを生成するスクリプトです。

生成位置のX座標は指定範囲内でランダムに決定されます。必要に応じて，初速度やX方向速度のランダム化も設定できます。

主な役割は以下の通りです。

* ボールPrefabを一定間隔で生成する
* 生成位置をランダム化する
* `Rigidbody2D` に初速度を設定する

### `PPPKillZone.cs`

画面外に落下したボールなどを削除するためのスクリプトです。

`OnTriggerEnter2D` により，Kill Zoneに入ったオブジェクトを破棄します。

### `BallSoundEffect.cs`

ボールが何かに衝突したときに，効果音とパーティクルを再生するスクリプトです。

`OnCollisionEnter2D` により，衝突時に以下を実行します。

* `ParticleSystem` の再生
* `AudioSource` に設定された効果音の再生

## Requirements

### Unity

* Unity 2Dプロジェクト
* Rigidbody2D / Collider2D を用いた2D物理環境
* UDP受信用のC#スクリプト

使用Unityバージョンは，プロジェクトに合わせて記載してください。

```text
Unity: 2022.3 LTS など
```

### Python

Python側では以下のライブラリを使用します。

* Python 3
* OpenCV
* NumPy

インストール例は以下の通りです。

```bash
pip install opencv-python numpy
```

## Setup

### 1. Unity Scene Setup

Unity側では，以下のような構成を用意します。

```text
Scene
├─ Main Camera
├─ PPPSystem
│  ├─ PPPUdpReceiver
│  └─ PPPObstacleManager
├─ BallSpawner
├─ KillZone
└─ BallPrefab
```

### 2. PPPSystem

空のGameObjectを作成し，以下のスクリプトを追加します。

* `PPPUdpReceiver.cs`
* `PPPObstacleManager.cs`

`PPPUdpReceiver` の `Obstacle Manager` には，同じGameObjectまたは別GameObject上の `PPPObstacleManager` を指定します。

ポート番号は，Python側と合わせます。

```text
listenPort = 5005
```

### 3. BallSpawner

空のGameObjectを作成し，`PPPBallSpawner.cs` を追加します。

`ballPrefab` には，以下のようなコンポーネントを持つボールPrefabを指定します。

* `Rigidbody2D`
* `CircleCollider2D`
* `SpriteRenderer`
* 必要に応じて `BallSoundEffect.cs`

### 4. KillZone

画面下部などに，ボール削除用のTrigger領域を配置します。

Kill Zone用のGameObjectには，以下のような設定を行います。

* `BoxCollider2D`
* `Is Trigger` を有効化
* `PPPKillZone.cs` を追加

### 5. Camera Settings

Unityのカメラは，シミュレーション領域と対応しやすいように設定します。

`PPPObstacleManager` のデフォルト設定では，シミュレーション領域は `16 x 9` です。そのため，Orthographic Cameraを用いる場合，画面比率16:9に合わせると扱いやすくなります。

例：

```text
Projection: Orthographic
Size: 4.5
```

この場合，縦方向が `9` world units になり，横方向は16:9表示で約 `16` world units になります。

## Running

### 1. Start Unity

Unityのシーンを再生します。

`PPPUdpReceiver` が正常に起動すると，Consoleに以下のようなログが表示されます。

```text
[PPP] UDP receiver started on port 5005
```

### 2. Start Python Sender

Pythonスクリプトを実行します。

ファイル名が `ppp_sender.py` の場合，以下のように実行します。

```bash
python ppp_sender.py
```

必要に応じて，送信先やカメラ番号を指定できます。

```bash
python ppp_sender.py --host 127.0.0.1 --port 5005 --camera 0
```

### 3. Calibrate Projection Area

Pythonのカメラウィンドウ上で，投影映像またはモニター表示領域の四隅をクリックします。

クリック順は以下の通りです。

```text
左上 → 右上 → 右下 → 左下
```

4点をクリックすると，射影変換後の `PPP Warped` ウィンドウと，付箋検出用の `PPP Mask` ウィンドウが表示されます。

### 4. Place Sticky Notes

表示面にピンク色の付箋を貼ると，Python側で検出されます。

検出された付箋はUnityに送信され，Unity内に対応する障害物として生成されます。ボールがその障害物に衝突することで，付箋の位置や角度に応じて物理挙動が変化します。

## Python Controls

Python実行中は，以下のキー操作が使えます。

```text
q : 終了
c : キャリブレーションをやり直す
s : 現在のキャリブレーションを保存する
l : 保存済みキャリブレーションを読み込む
```

## Command Line Options

Pythonスクリプトでは，以下のオプションを指定できます。

```bash
python ppp_sender.py --host 127.0.0.1 --port 5005 --camera 0 --width 1280 --height 720
```

主なオプションは以下の通りです。

| Option      | Description     | Default                |
| ----------- | --------------- | ---------------------- |
| `--host`    | Unityの送信先IPアドレス | `127.0.0.1`            |
| `--port`    | Unityの受信ポート     | `5005`                 |
| `--camera`  | 使用するカメラ番号       | `0`                    |
| `--width`   | 射影変換後の横幅        | `1280`                 |
| `--height`  | 射影変換後の縦幅        | `720`                  |
| `--calib`   | キャリブレーション保存ファイル | `ppp_calibration.json` |
| `--no-save` | 自動保存を無効化        | `false`                |

## Communication Format

PythonからUnityへ送信されるJSONは以下の形式です。

```json
{
  "frame_w": 1280,
  "frame_h": 720,
  "obstacles": [
    {
      "id": 0,
      "x": 0.5,
      "y": 0.4,
      "w": 0.1,
      "h": 0.05,
      "angle": 30.0
    }
  ]
}
```

各値の意味は以下の通りです。

| Field       | Description         |
| ----------- | ------------------- |
| `frame_w`   | Python側の射影変換後画像の横幅  |
| `frame_h`   | Python側の射影変換後画像の縦幅  |
| `obstacles` | 検出された付箋の配列          |
| `id`        | 障害物ID               |
| `x`         | 中心X座標。0.0から1.0の正規化値 |
| `y`         | 中心Y座標。0.0から1.0の正規化値 |
| `w`         | 幅。0.0から1.0の正規化値     |
| `h`         | 高さ。0.0から1.0の正規化値    |
| `angle`     | 矩形の角度               |

## Notes

現在のプロトタイプでは，検出対象をピンク色の付箋に限定しています。そのため，照明環境やカメラの色味によっては，HSVのしきい値を調整する必要があります。

```python
LOWER_HSV = np.array([140, 60, 60], dtype=np.uint8)
UPPER_HSV = np.array([179, 255, 255], dtype=np.uint8)
```

また，現在のIDはフレームごとの輪郭検出結果に基づいて割り当てられます。そのため，複数の付箋を同時に扱う場合，検出順が変化するとUnity側の障害物IDが入れ替わる可能性があります。現状では簡易的なプロトタイプとして扱い，より安定した運用には，位置の近さに基づくトラッキング処理などを追加することが考えられます。

## Future Work

今後の発展案として，以下のような機能が考えられます。

* 付箋の色ごとに異なる効果を割り当てる

  * 通常の壁
  * 強く跳ね返す壁
  * ボールを加速させる領域
  * ボールを引き寄せる領域
* 複数人で同時に付箋を配置する協力型体験
* ゴールやスコアを追加したゲーム化
* 付箋以外の実物体の検出
* マーカーやQRコードを用いたより安定した物体識別
* 物体の追跡処理によるIDの安定化
* 展示環境向けの自動キャリブレーション

## Repository Structure

リポジトリ構成の例は以下の通りです。

```text
PPP/
├─ README.md
├─ UnityProject/
│  └─ Assets/
│     └─ Scripts/
│        ├─ PPPUdpReceiver.cs
│        ├─ PPPObstacleManager.cs
│        ├─ PPPBallSpawner.cs
│        ├─ PPPKillZone.cs
│        └─ BallSoundEffect.cs
├─ Python/
│  └─ ppp_sender.py
└─ docs/
   └─ images/
      └─ demo.gif
```

## Author

Kentaro Fujii

## License

ライセンスを設定する場合は，ここに記載してください。

例：

```text
MIT License
```

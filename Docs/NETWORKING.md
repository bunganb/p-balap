# Panduan Networking P, Balap!

Dokumen ini adalah pegangan anggota tim saat menghubungkan fitur gameplay ke network. Implementasi saat ini menggunakan Unity 6, Netcode for GameObjects (NGO) 2.13.2, Unity Transport, Authentication anonymous, dan Unity Relay.

## Arsitektur singkat

```text
Host                                     Client
Initialize Unity Services                Initialize Unity Services
  -> anonymous sign-in                     -> anonymous sign-in
  -> create Relay allocation               -> join allocation dengan kode
  -> mendapatkan join code                 -> konfigurasi Unity Transport
  -> NGO StartHost                          -> NGO StartClient
                 \                        /
                  Relay melalui DTLS/UDP
```

Host juga menjadi server authoritative. Client meminta aksi; Host memvalidasi, mengubah state, lalu NGO menyebarkan hasilnya. Relay menghindari kebutuhan membuka port router. Gameplay desktop memakai DTLS (UDP terenkripsi), bukan koneksi TCP langsung. HTTPS hanya dipakai SDK untuk Authentication dan pembuatan/join allocation.

## Setup Unity Gaming Services

1. Buka project melalui Unity Hub menggunakan Unity Editor yang sama dengan tim.
2. Pastikan project terhubung ke Unity Cloud Project ID yang benar melalui `Edit > Project Settings > Services`.
3. Project ID repository saat ini tersimpan pada `ProjectSettings/ProjectSettings.asset`. Jangan menggantinya dengan project pribadi saat commit.
4. Pada Unity Dashboard, aktifkan Relay untuk Cloud Project tersebut.
5. Pastikan package berikut tetap kompatibel: NGO, Unity Transport, Authentication/Core, dan Relay.
6. Jangan memasang `com.unity.netcode` (Netcode for Entities) untuk fitur ini. Project menggunakan `com.unity.netcode.gameobjects`.

Jika muncul `InactiveProject`, `Unauthorized`, atau gagal allocation, cek Cloud Project ID, organisasi, dashboard Relay, login Unity Editor, dan koneksi internet.

## Membuat dan masuk room

Gunakan scene `Assets/Scenes/Network.unity`.

### Host

1. Play.
2. Tekan **Create Room (Host)**.
3. Tunggu status `Hosting`.
4. Bagikan join code kepada Client.
5. Panel Host menampilkan jumlah pemain, daftar Client ID, dan aktivitas join/leave.

Allocation dibuat untuk tiga Client, sehingga kapasitas total adalah empat pemain termasuk Host.

### Client

1. Play pada build/laptop lain.
2. Masukkan join code.
3. Tekan **Join Room (Client)**.
4. Tunggu status `Joined`.

Kode kosong dan format salah ditolak sebelum request. Kode yang kedaluwarsa, room penuh, Host keluar, timeout, dan gangguan internet ditampilkan sebagai pesan, bukan crash.

## Lifecycle dan disconnect

- **Leave Room (Client)** menjalankan shutdown lokal dan kembali ke panel awal Network.
- **Close Room (Host)** memutus session. Semua Client menerima disconnect dari transport.
- Ketika Host hilang tanpa menutup secara normal, Client menampilkan bahwa Host keluar dan memanggil event `ReturnToMenuRequested`.
- Saat ini scene Main Menu final belum tersedia. “Kembali ke menu” berarti kembali ke panel Create/Join pada scene Network. UI menu final dapat subscribe ke `ReturnToMenuRequested` lalu melakukan navigasi.
- Authentication tidak di-sign-out saat meninggalkan room agar pemain dapat membuat/join room baru tanpa login ulang.
- Relay allocation lama berakhir ketika Host berhenti dan tidak dipakai kembali. Room berikutnya selalu memakai allocation dan join code baru.

API yang dapat dipakai UI final:

```csharp
sessionController.StateChanged += HandleStateChanged;
sessionController.ClientConnected += HandleClientConnected;
sessionController.ClientDisconnected += HandleClientDisconnected;
sessionController.NoticeChanged += ShowNotification;
sessionController.ReturnToMenuRequested += ReturnToMenu;

await sessionController.CreateRoomAsync();
await sessionController.JoinRoomAsync(code);
sessionController.LeaveRoom();
sessionController.CloseRoom();
```

Selalu unsubscribe ketika UI dinonaktifkan.

## Mengapa sebelumnya Host terlihat tidak menerima data join?

`NetworkManager.OnClientConnectedCallback` sebenarnya sudah diterima Host. Sebelumnya callback hanya menulis ke Console dan belum ada Player Prefab, sehingga tidak ada object visual atau notifikasi layar. Sekarang panel Host menampilkan:

- `Players: current/4`;
- Host dan Client ID yang sedang terhubung;
- notifikasi saat Client bergabung atau keluar;
- lima aktivitas network terbaru.

Player baru belum terlihat sebagai kendaraan sampai Player Prefab didaftarkan.

## Menambah data network

| Kebutuhan | Gunakan |
| --- | --- |
| Hanya untuk perangkat lokal | field C# biasa |
| State terbaru harus diterima semua peer dan late joiner | `NetworkVariable<T>` |
| Request/event satu kali | `[Rpc]` |
| Object harus ada pada semua peer | `NetworkObject.Spawn()` oleh Host |
| Posisi/rotasi | `NetworkTransform` atau controller movement network tim |

Contoh state authoritative:

```csharp
public sealed class NetworkRacerState : NetworkBehaviour
{
    public NetworkVariable<int> CurrentLap = new(0);

    public void AddLapOnServer()
    {
        if (IsServer)
        {
            CurrentLap.Value++;
        }
    }
}
```

Contoh Client meminta aksi:

```csharp
[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
private void RequestUseItemRpc(int slot, RpcParams rpcParams = default)
{
    ulong senderId = rpcParams.Receive.SenderClientId;
    // Host wajib memvalidasi owner, item, jarak, dan cooldown.
}
```

Jangan membuat `ServerRpc` baru karena pola tersebut deprecated pada versi NGO project. Jangan mengirim RPC setiap `Update()` jika state dapat dikirim hanya ketika berubah.

## Memasang Player Prefab

Prefab kendaraan dikerjakan pemilik fitur player/vehicle. Owner network membantu integrasi berikut:

1. Tambahkan `NetworkObject` pada root prefab.
2. Tambahkan komponen transform network yang disepakati tim.
3. Pastikan input, kamera, dan AudioListener hanya aktif untuk `IsOwner`.
4. Simpan state bersama dalam `NetworkVariable`; request gameplay dikirim ke Host.
5. Daftarkan prefab pada Network Prefab List milik `NetworkManager`.
6. Isi `PlayerPrefab` pada `NetworkManager` di scene Network.
7. Test bahwa Host dan Client mendapat `OwnerClientId` berbeda dan hanya mengontrol player sendiri.

Jangan membuat `NetworkManager` kedua di scene race. Bootstrap dari scene Network dibuat persisten.

## Memasang item network

1. Tambahkan `NetworkObject` pada root Item Prefab.
2. Daftarkan prefab pada Network Prefab List.
3. Host melakukan `Instantiate`, lalu `NetworkObject.Spawn()`.
4. Client mengirim request pickup kepada Host.
5. Host memvalidasi pengirim, jarak, status item, dan cooldown.
6. Host mengubah inventory/effect lalu memanggil `NetworkObject.Despawn()`.
7. Jangan memanggil `Destroy` langsung untuk object yang sudah network-spawn.

Random spawn, respawn timer, hasil tabrakan, lap, score, dan kemenangan harus diputuskan Host agar semua Client konsisten.

## Pengujian dua laptop

Kedua laptop harus memakai commit/branch, Unity version, package lock, dan Cloud Project yang sama. Internet wajib tersedia.

| Test | Hasil yang diharapkan |
| --- | --- |
| Host membuat room | Status `Hosting`, join code muncul, roster berisi Host. |
| Client join | Client `Joined`; Host menampilkan Client ID dan jumlah 2/4. |
| Client menekan Leave | Client kembali ke panel Create/Join; Host menampilkan Client keluar dan jumlah 1/4. |
| Client dihentikan paksa | Host menerima callback disconnect dan menghapus Client dari roster. |
| Host menekan Close | Host kembali ke panel Create/Join; Client mendapat pesan Host keluar. |
| Host dihentikan paksa | Client mendapat pesan disconnect dan `ReturnToMenuRequested`. |
| Buat room lagi | Join code baru muncul dan tidak membawa roster session lama. |
| Kode salah | Pesan jelas dan Console tidak memiliki exception yang tidak ditangani. |
| Room penuh | Client tambahan gagal dengan pesan room penuh/tidak menerima pemain. |

Sesudah Player Prefab dipasang, ulangi test sambil memastikan spawn, ownership, movement, dan despawn pemain konsisten.

## Aturan kolaborasi

- Koordinasikan perubahan scene Network dan Network Prefab List dengan owner network untuk menghindari merge conflict YAML.
- Jangan upgrade package network sendiri.
- Jangan commit `Library`, `Temp`, `Logs`, atau build output.
- Pisahkan script logic dari visual prefab agar pekerjaan lima anggota dapat di-merge lebih aman.
- Setiap fitur network wajib diuji minimal dengan satu Host dan satu Client build terpisah.

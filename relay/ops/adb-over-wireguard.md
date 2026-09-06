# Deploying to a phone remotely, over the WireGuard tunnel

**Status (2026-09-06): steps 1-2 confirmed working on the Galaxy S9+** — `adb tcpip 5555` +
`adb connect <ip>:5555` successfully replaced the USB connection while the phone was still on the
home LAN's WiFi (`192.168.50.236` in that test). Not yet tried over an actual WireGuard tunnel from
outside the LAN (needs the S23+ on USB briefly first, and the PC-side route in step 3) — that's the
remaining piece to actually prove step 3 end to end.


Normal deploys (`dotnet build ... -f net10.0-android -t:Run`) go over `adb`, which needs the phone
reachable by IP from this PC — true over USB or the same LAN, not over mobile data. Since a phone's
WireGuard tunnel gives it a real IP inside the home network, `adb` can work over that tunnel too,
once wireless debugging is turned on and the PC knows how to route to the WireGuard subnet.

## One-time setup (needs the phone on USB, even just briefly)

1. With the phone connected over USB and `adb devices` seeing it:
   ```powershell
   $adb = "C:\Users\dvora\AppData\Local\Android\Sdk\platform-tools\adb.exe"
   & $adb tcpip 5555
   ```
   This switches the phone's adb daemon to also listen over the network (any interface, including
   its WireGuard `wg0`/`tun` interface) on port 5555 — it keeps listening there even after the
   phone reboots or the USB cable comes out, until wireless debugging is turned off in Developer
   Options.

2. Find the phone's WireGuard-assigned IP — check the wg-easy admin panel
   (`http://192.168.50.8:51821`), the client list shows each peer's assigned address (commonly in
   the `10.8.0.0/24` range — confirm the actual value there rather than assuming).

3. On the PC, add a route so it knows `10.8.0.0/24` (adjust to the real subnet from step 2) is
   reachable via the Pi's LAN IP:
   ```powershell
   route add 10.8.0.0 mask 255.255.255.0 192.168.50.8
   ```
   This is a one-time, PC-local route (survives reboots on Windows by default — no `-p` needed
   unless it doesn't). If the PC and Pi ever move to a different LAN/subnet, redo this step.

## Each time you want to deploy while the phone is remote (WireGuard active, no USB)

```powershell
$adb = "C:\Users\dvora\AppData\Local\Android\Sdk\platform-tools\adb.exe"
& $adb connect <phone-wireguard-ip>:5555
& $adb devices -l   # should show the phone, now over the tunnel instead of USB
```

Then deploy exactly as usual:
```powershell
dotnet build "src\SecureApp.Presentation\SecureApp.Presentation.csproj" -f net10.0-android -t:Run
```

## If it doesn't connect

- Confirm the phone's WireGuard tunnel is actually active (not just installed) — check the
  WireGuard app on the phone.
- Confirm the route landed: `route print | findstr 10.8.0.0` (adjust subnet) on the PC.
- Wireless debugging can time out/turn off after an extended period offline or a phone reboot —
  redo step 1 (needs USB again) if `adb connect` refuses the connection outright rather than just
  timing out.
- The Pi's own routing must allow LAN→VPN traffic, not just VPN→LAN (which is all wg-easy sets up
  by default for internet-sharing purposes) — if the PC's `route add` above doesn't result in a
  working connection, the fix likely belongs on the router (a static route for the WireGuard
  subnet pointing at the Pi's LAN IP, `192.168.50.8`) rather than only on this one PC.

# Mobile browser access

DBLC's optional mobile browser interface runs inside the Windows desktop process. It shares that process's active campaign, Session, Chronicle, and application state; it is not a separately hosted multiplayer service. The host is disabled by default and listens on loopback. DBLC does not configure a remote tunnel.

## Private tailnet setup

1. Install and sign in to Tailscale on the PC and phone, using the same tailnet.
2. In DBLC, open **Settings → General → Mobile Web**, create an access code, then enable **Mobile browser access**. Enter the code in the phone browser when prompted.
3. In a Windows terminal, run `tailscale serve 8765`, then check `tailscale serve status` for the private HTTPS address. Use Tailscale Serve only; do not enable Funnel or expose the DBLC port publicly.
4. Open the HTTPS address on the phone. To stop remote access, run `tailscale serve 8765 off` and disable Mobile browser access in DBLC Settings.

DBLC's browser host remains loopback-only and shuts down with the desktop application. Mobile availability and interaction vary by screen and workflow; native Windows file selection and some management operations remain desktop-oriented. Test remote access on your own tailnet and device before relying on it.

Consult Tailscale's current documentation for command changes and tailnet policy requirements.

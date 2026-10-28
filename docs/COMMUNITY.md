# Community access and notices

NullServers 0.0.37 checks the collective blacklist independently on the client and on the room host. There is no settings switch, administrator command or editable local blacklist file that disables this check. Official ZeoWorks rooms do not use it.

## Maintaining the blacklist

Edit this UTF-8 plain text document:

https://raw.githubusercontent.com/Nullframe-Collective/Nullframe-Blacklist/refs/heads/main/Blacklist.txt

Use one complete ZeoWorks account name per line. Matching ignores case and surrounding whitespace; it does not match substrings. Empty lines and lines beginning with `#` or `//` are ignored. Names containing spaces or square brackets are supported. An intentionally empty list is valid. The extra period after `Blacklist.txt` in the original supplied link is not part of the working URL.

The client checks the identity supplied by the authenticated relay, rather than the nickname selected in the lobby. The host separately checks authenticated connections using the relay's player identities. A local host unban does not override a collective ban. Collective entries are not copied into the host's permanent local bans, so removing an entry upstream can take effect on the next successful refresh.

A successful list is kept in memory and refreshed every five minutes, with an additional refresh when entering NullFrame Servers. Failed refreshes retry after 30 seconds and preserve the last successful list. A new game session without a successful download cannot create or join a NullFrame room. The player sees a connection explanation and can retry. Downloading an updated list also checks users already connected.

Rejected players see: `You have been banned from NullFrame Collective servers.` The host sends this explanation and invokes the native disconnection path. The normal disconnect may also briefly show the game's connection notification.

This is enforcement by participating NullServers clients and hosts, not a change to the central relay service. Modifying both sides of the DLL, using another account, or using a guest identity is outside the guarantees of an account-name blacklist. A modified client cannot bypass an unmodified host's independent account check by changing its local nickname or acknowledgement settings.

## Rules and Update Log

On the first visit through **NullFrame Servers**, the built-in English Rules window appears first. It covers responsible mod/cheat use, disruption, harassment, advertising and privacy. Pressing **OK** saves acceptance. Subsequent visits do not repeat these rules.

The update text is fetched from:

https://pastebin.com/raw/Ph2u8AG8

An unseen document opens as **Update Log**, after Rules when both are pending. The window uses the native game font, a scrollable document and a fixed bottom **OK** button. Its normalized SHA-256 revision is stored only after OK. A changed document opens again; unchanged content and equivalent newline encodings do not. Empty text or a failed update download does not overwrite the last accepted revision or prevent access once the blacklist is available.

Acceptance uses Unity `PlayerPrefs`, stored in the game's Windows registry preferences under these keys:

- `NullFrame.Community.RulesAccepted`
- `NullFrame.Community.UpdateRevision`

Only these acknowledgements are stored there; no local preference disables the blacklist. Rules text is in `Additional/CommunityDocument.cs`; the external Pastebin controls Update Log text only.

## Remote document handling

Both requests use fixed HTTPS endpoints with normal certificate verification, a 15-second timeout, no redirects or cookies, and bounded response streaming. Limits are 256 KiB for the blacklist and 64 KiB for Update Log. Responses must be valid UTF-8 `text/plain`. Malformed lists are rejected atomically and cannot replace a valid list partially. The UI displays literal text with rich text disabled. Neither endpoint downloads DLLs, asset bundles or executable commands.

## Validation

The release passes 1,257 automatic checks, including blacklist parsing and failed refreshes, upstream removal, document limits and decoding, acknowledgement ordering, changed content and text block boundaries. Local game checks covered first-visit Rules, the live Update Log, re-entry, long-document scrolling, normal private room creation and the actual account-ban denial/return-to-menu path. Host enforcement uses authenticated sender identities and the existing native kick path; a separate-computer test of a modified client bypassing its own check remains outstanding.

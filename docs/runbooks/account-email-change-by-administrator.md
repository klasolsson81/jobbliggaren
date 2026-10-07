# Changing an account's address for its owner

An account's owner who can no longer receive mail at the account's address cannot use Mina sidor → Byt
e-postadress, which proves both inboxes. An administrator can start the change instead (#1975, ADR 0153). The
change completes only when the owner enters the account's current address, the new address and the code mailed to
the new address on `/adressbyte`, and not before the delay has run. The account's current address is told when the
change starts, so its holder can object while the change waits.

This runbook is the procedure around that flow. The flow's own stops are in ADR 0153.

## When to use it

- Only on a written request to kontakt@jobbliggaren.se. That thread is the record and is kept 12 months. Never act on
  a call or a chat alone.
- If the requester can still receive mail at the account's address, refuse and point them to Mina sidor → Byt
  e-postadress.
- Administrator accounts, your own included, are refused by you and by the system.

## Verifying a requester

The account stores no name (ADR 0142 D7), so a name proves nothing about who owns it.

1. **Never ask for, accept or store an identity document, a personnummer or anything else the account does not hold.**
   Collecting one is a processing with no purpose here.
2. **The requester names the account's current address themselves.** Compare it as login does, ignoring letter case.
   Without it, do not start the change; with it, you still need more.
3. What may support a request beyond that, never decisive on its own: facts the directory shows without opening any
   content (when the account was registered, whether it has CVs or applications). Never open a CV or an application
   to verify.
4. **Never name, confirm, correct or hint at an account's address, and never say whether an address has an account or
   what kind of account it is.** Word every refusal the same, whatever its reason.
5. If you cannot tell, do not start the change.

This is the one statement of the rule. Other runbooks point here.

## Starting the change

1. Copy the new address from the request (the address it came from, or the one stated in it); never retype it.
   Confirm it in the thread.
2. In `/admin/anvandare`, open the account and choose "Ändra e-postadress". Enter the new address and press
   "Fortsätt". A code goes to your own address; enter it to send the request.
3. The panel then shows the pending change and when it can be completed at the earliest. Tell the requester that
   they need the account's current address and the code that was sent to the new address, and when they can use it.
   Never ask for the code and never send it.

If the request's answer is lost or the panel reports an unknown outcome, do not automatically repeat it. Open the
account again to inspect current state. A pending row is a storage snapshot, not a receipt that this particular
command committed or that both mails were accepted. If a change waits that you cannot account for, cancel it and
start again with a new code to your own inbox and both request mails; the full delay starts over.

Completion requires the exact committed request witness for that v2 change, as well as its original account access
generation. A Redis record alone cannot activate it. A lost mail reply does not prove the recipient got nothing;
without the committed witness any surviving record or delivered code remains unusable. An unknown activation
commit may have committed that witness, so it must not be described as a confirmed cancellation or failure.
The server checks the random request id against the exact committed `Admin.AccountEmailChangeRequested` event,
`User` aggregate and target within the proof's original 96-hour lifetime, under the same account lock and
transaction as the swap. No address or credential is added to that audit payload (ADR 0153's 2026-10-07 amendment and
[ADR 0155](../decisions/0155-account-access-transitions-fence-sessions-and-original-authentication-proofs.md)).

The server releases the first short account transaction and every lock before sending the current-address warning
and then the new-address code. Both mails must be accepted. A second short transaction rechecks the original
administrator/session authority, Admin role, target generation, current address, exact pending request, original
lifetime and destination availability before committing activation. It does not refresh the original proof while
mail transport waits. A send failure or known rollback leaves any surviving record inert even if cleanup fails.

Completion changes user name and email, advances the target's credential generation and records its completion
audit in one transaction. It leaves suspension state unchanged and issues no session; the owner must log in at
the new address. After a known commit, failed Redis cleanup cannot turn the real address-change receipt into a
refusal. Cleanup removes only older generations, preserving a fresh login. The old-address completion notice is
sent only after that commit. An unknown commit supplies no success receipt and is never automatically replayed.

## While the change waits

- An objection from the current address, or any doubt, means cancel: "Avbryt adressbytet" in the panel. Cancelling
  needs no code.
- If two people claim one account, change nothing and cancel. The controller decides, never by who is more
  persuasive.
- A restart of the volatile Redis instance cancels every pending change. The panel then shows none; start again if
  the request still stands. The current address is told again and the delay starts over.
- Suspending the account permanently invalidates its pending change and any already consumed proof. Reinstating
  access does not revive either. A new change may be started only when the account is active again.
- During the #1976 rollout, genuine v1 pending admin changes are hidden from the panel and receive the public
  page's generic 410, including for accounts that have never been suspended. Restart with a fresh inbox step-up,
  both newly accepted request mails and a new full 72-hour delay. An old code cannot complete the restarted change.

## Never

- Never change an address by SQL or by any path other than the flow. `StoredAddressWriterGuardTests` guards the code;
  this rule guards the operator.

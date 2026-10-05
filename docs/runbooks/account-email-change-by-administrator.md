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
2. What may support a request, never decisive on its own: the requester names the account's current address exactly,
   or states facts the directory shows without opening any content (when the account was registered, whether it has
   CVs or applications). Never open a CV or an application to verify.
3. If you cannot tell, do not start the change.

This is the one statement of the rule. Other runbooks point here.

## Starting the change

1. Copy the new address from the request (the address it came from, or the one stated in it); never retype it.
   Confirm it in the thread.
2. In `/admin/anvandare`, open the account and choose "Ändra e-postadress". Enter the new address and press
   "Fortsätt". A code goes to your own address; enter it to send the request.
3. The panel then shows the pending change and when it can be completed at the earliest. Tell the requester that
   they need the account's current address and the code that was sent to the new address, and when they can use it.
   Never ask for the code and never send it.

If the request's answer is lost (the panel says it cannot see whether the code was sent), open the account again:
the pending row shows whether a change waits. If one waits that you cannot account for, cancel it and start again.

## While the change waits

- An objection from the current address, or any doubt, means cancel: "Avbryt adressbytet" in the panel. Cancelling
  needs no code.
- If two people claim one account, change nothing and cancel. The controller decides, never by who is more
  persuasive.
- A restart of the volatile Redis instance cancels every pending change. The panel then shows none; start again if
  the request still stands. The current address is told again and the delay starts over.

## Never

- Never change an address by SQL or by any path other than the flow. `StoredAddressWriterGuardTests` guards the code;
  this rule guards the operator.

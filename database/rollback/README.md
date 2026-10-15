# Rollback scripts

For each reversible migration `V000N__x.sql`, a matching `V000N__x.rollback.sql` may live here. Production recovery for risky changes
is a point-in-time restore taken before the release; rollback scripts cover the safe cases.

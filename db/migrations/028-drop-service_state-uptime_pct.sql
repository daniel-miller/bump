-- 028: Drop service_state.uptime_pct. The prober wrote a synthetic score
-- there every cycle - the last ~60 probes, clamped to [95, 100], docked for
-- degraded as well as down - and status boards showed it as uptime. Every
-- reader now computes availability from probe_event instead, so the column
-- was written and never read.

ALTER TABLE service_state DROP COLUMN IF EXISTS uptime_pct;

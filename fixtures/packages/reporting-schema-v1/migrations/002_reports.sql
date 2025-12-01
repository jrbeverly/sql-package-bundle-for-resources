-- This migration is
-- deliberately defective — `rs_report` does not exist — so the server
-- rejects it. The repaired copy lives in fixtures/packages/reporting-schema-fixed.
INSERT INTO `rs_report` (`entry`, `name`, `dataset_entry`) VALUES
  (600101, 'Monthly Sales', 600001),
  (600102, 'Daily Usage', 600002);

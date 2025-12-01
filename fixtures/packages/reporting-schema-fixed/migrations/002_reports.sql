-- The repaired copy of the deliberately defective migration in
-- fixtures/packages/reporting-schema-v1.
SET @REPORT_SALES := 600101;
SET @REPORT_USAGE := 600102;

CREATE TABLE IF NOT EXISTS `rs_report` (
  `entry` INT UNSIGNED NOT NULL,
  `name` VARCHAR(64) NOT NULL,
  `dataset_entry` INT UNSIGNED NOT NULL,
  PRIMARY KEY (`entry`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

INSERT INTO `rs_report` (`entry`, `name`, `dataset_entry`) VALUES
  (@REPORT_SALES, 'Monthly Sales', 600001),
  (@REPORT_USAGE, 'Daily Usage', 600002);

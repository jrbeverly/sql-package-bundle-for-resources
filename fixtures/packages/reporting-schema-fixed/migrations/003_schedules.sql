-- Delivery schedules for generated reports.
SET @SCHEDULE_MONTHLY := 600201;
SET @SCHEDULE_DAILY := 600202;

CREATE TABLE IF NOT EXISTS `rs_schedule` (
  `entry` INT UNSIGNED NOT NULL,
  `name` VARCHAR(64) NOT NULL,
  `report_entry` INT UNSIGNED NOT NULL,
  PRIMARY KEY (`entry`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

INSERT INTO `rs_schedule` (`entry`, `name`, `report_entry`) VALUES
  (@SCHEDULE_MONTHLY, 'Monthly Delivery', 600101),
  (@SCHEDULE_DAILY, 'Daily Delivery', 600102);

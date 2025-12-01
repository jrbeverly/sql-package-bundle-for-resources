-- Reporting datasets available to later migrations.
-- Deliberately not idempotent: re-executing this file would fail on the
-- primary key, which is how the re-run test proves it ran exactly once.
SET @DATASET_SALES := 600001;
SET @DATASET_USAGE := 600002;

CREATE TABLE IF NOT EXISTS `rs_dataset` (
  `entry` INT UNSIGNED NOT NULL,
  `name` VARCHAR(64) NOT NULL,
  `refresh_minutes` SMALLINT UNSIGNED NOT NULL DEFAULT 0,
  PRIMARY KEY (`entry`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

INSERT INTO `rs_dataset` (`entry`, `name`, `refresh_minutes`) VALUES
  (@DATASET_SALES, 'Sales Summary', 60),
  (@DATASET_USAGE, 'Usage Summary', 15);

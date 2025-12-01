-- Customer profiles that the package manages.
SET @CUSTOMER_NORTH := 400001;
SET @CUSTOMER_SOUTH := 400002;

CREATE TABLE IF NOT EXISTS `cp_customer_profile` (
  `entry` INT UNSIGNED NOT NULL,
  `name` VARCHAR(64) NOT NULL,
  `status` SMALLINT UNSIGNED NOT NULL DEFAULT 0,
  PRIMARY KEY (`entry`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

DELETE FROM `cp_customer_profile` WHERE `entry` IN (@CUSTOMER_NORTH, @CUSTOMER_SOUTH);

INSERT INTO `cp_customer_profile` (`entry`, `name`, `status`) VALUES
  (@CUSTOMER_NORTH, 'Northwind Supplies', 1),
  (@CUSTOMER_SOUTH, 'Southridge Services', 1);

UPDATE `cp_customer_profile` SET `status` = 2 WHERE `entry` = @CUSTOMER_SOUTH;

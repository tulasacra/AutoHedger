# AutoHedger


Requirements:
- https://nodejs.org


Sample output:

```
Checking at: 24/10/2024 11:01:19
Minimum desired APY: 5 %
Reading contracts ..OK
Reading premiums ..OK

====================================================================================================

BCH acquisition cost FIFO:             376.22000000 USD
Latest price from OraclesCash:         350.14000000 USD (-6.93 %)
-------------------------------------------------------------------------------------
|                           |               BCH |               USD |       % |
|---------------------------|-------------------|-------------------|---------|
| Wallet balance:           |        0.28295619 |             99.07 |   49.77 |
| Active contracts balance: |        0.28560005 |            100.00 |   50.23 |
| Total balance:            |        0.56855624 |            199.07 |         |
-------------------------------------------------------------------------------------
| Amount (BCH) | Duration (days) | Premium (%) | APY (%) | APY + price diff (%) |
|--------------|-----------------|-------------|---------|----------------------|
|            1 |              30 |       -1.09 |   14.10 |                 7.17 |
|           10 |              30 |       -1.05 |   13.55 |                 6.62 |
|          100 |              30 |       -0.65 |    8.20 |                 1.27 |
|            1 |              60 |       -4.21 |   28.51 |                21.58 |
|           10 |              60 |       -4.17 |   28.21 |                21.28 |
|          100 |              60 |       -3.82 |   25.62 |                18.68 |
|            1 |              90 |       -5.22 |   22.92 |                15.99 |
|           10 |              90 |       -5.18 |   22.73 |                15.80 |
|          100 |              90 |       -4.82 |   21.04 |                14.10 |

====================================================================================================

BCH acquisition cost FIFO:                       ?? EUR
Latest price from OraclesCash:         324.31000000 EUR (?? %)
-------------------------------------------------------------------------------------
|                           |               BCH |               EUR |       % |
|---------------------------|-------------------|-------------------|---------|
| Wallet balance:           |                ?? |                ?? |      ?? |
| Active contracts balance: |        0.00000000 |              0.00 |      ?? |
| Total balance:            |                ?? |                ?? |         |

====================================================================================================
```
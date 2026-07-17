#!/bin/bash
# Downloads open-source art assets for STARFALL ODYSSEY.
# Sources:
#   - MillionthVector (Alan Guyant), CC-BY 4.0 — https://millionthvector.blogspot.com/p/free-sprites.html
#   - Kenney Particle Pack, CC0 — https://kenney.nl/assets/particle-pack
# See assets/ATTRIBUTION.md for details.
set -e
cd "$(dirname "$0")/.."
mkdir -p assets/ships assets/stations assets/fx

BV="https://blogger.googleusercontent.com/img/b/R29vZ2xl"

dl() { # dl <output> <url>
  if [ -s "$1" ]; then echo "skip $1"; else curl -sfL -o "$1" "$2" && echo "ok   $1"; fi
}

# ---- MillionthVector ships (CC-BY 4.0) ----
dl assets/ships/orangeship.png  "$BV/AVvXsEjUfg-eVZjNY7VSU7kVb7yiEKOdnRBzObs2xg8S-wKMONQDcZFOAh0cnm4FJHhKT2buML481YB0WxRnbAWPFGCMBJ569eJAEy8l9YcKFYSErGI7kwhM6LKDeNRqkvdSqT07qWELoaAdKOo/s1600/orangeship.png"
dl assets/ships/orangeship2.png "$BV/AVvXsEgkHPq5X0jsWjkqSeARqiCfzDa-Qf1L8rpJ9dwTjEdoZcf5J03-h87BSewgyvcRLtQFe4omcn1Z6-QO0F6TeGK09rTljsO6Vo8bU96OC5BShcW7nzjUJ8W8_uEny3lT7j2y7o4OBd9Pi8M/s1600/orangeship2.png"
dl assets/ships/orangeship3.png "$BV/AVvXsEholEqYC3ZoVXtpPS0to_-hyBVvDfoMnf-S3BKPC5O-hVKOdnrqtGItCkyNGRoGE_WU9dmwmnXVJxthvUguGqYsOLAXEJNRflX2zQY6LbjW89lSS5lHAGYgL4K8AxooP_DHPS1k1_UIkIg/s1600/orangeship3.png"
dl assets/ships/smallorange.png "$BV/AVvXsEgBcjLHpK0S8GgF9oqfXr3IpJgUhEMZzVh0zJHFN9Kb2ua53LCEfLCCQAWCQ_rdWQc-k00V9yBHp7GZvWBY-EIvUNb4LbYMiK16MOmpi68bViYThJsvmZ6l5WZjg_4x3vL_XubbEbjQV0g/s1600/smallorange.png"
dl assets/ships/blueship1.png   "$BV/AVvXsEj_2l2DbIOnmUe0nDNirryDVbQxubWso4-ofUt4UgU_G5yrXqpsmyG8tlnG7vVYGu1I5saTQiHHPBis9_yQ2u3T0EwbsSrJM90x4KpNNr63pgvyaPVf8TXddeYnt7wzYF1dPMweZnheTV0/s1600/blueship1.png"
dl assets/ships/blueship2.png   "$BV/AVvXsEj9D85s3F9cYeXstj3B-WAq4LkbwlP0MXEnQqChi6nCPujANeH9GBbzc5sKQXavr1puezQI5-cnVlj2uPq8UAZfWckb9zPiOUfJnf5vCBedQViySuBSQeLJdHKM8J5E96KWz2hJYuqXOhU/s1600/blueship2.png"
dl assets/ships/blueship3.png   "$BV/AVvXsEjrvALb_aUW4XpO3rJTdxQcpYeCcC-k_5dxWM6nEUyko5Y_QF9qEMfNiv4Me96Z2EeolxC-lvU-eTP42TEsbWN3f3Z7WEL49GmIUuKmO_qh1pIssMMcxPkBYucFbiO7_u4d7DpLvPoLdC8/s1600/blueship3.png"
dl assets/ships/blueship4.png   "$BV/AVvXsEjSviTiTRgLYGNpYZW9qJQpbG5mPmOEvyEZUqEFVlyP5GBpWWGGXVEcL3EimZN0GxSkQfPTuZQ4U4RPuSYhHg5DrJZ1JeSYY7PlDclgosP_Fh5-q9UiHcMOI0LbysZmllXPZiE0hw-vJqI/s1600/blueship4.png"
dl assets/ships/alien1.png      "$BV/AVvXsEg6BXhrORAxBKajcHev9xg0d87daek_Z5FJCAvdkLklOpewlDIi0uMb4SPgoy3PYYX3l-1SOBskRk8y53N8jkJmTNfNe_o4X5ti-6M7Wytd6ADzdsV5BR1jHS4HQzaYPIgoKOvTWwlycQ8/s1600/alien1.png"
dl assets/ships/alien2.png      "$BV/AVvXsEhp3o5kd-IYpvci9tj3Rp39drwwE0lU4z68JHduO-bp0Otg9nO_5TCIU4d6VdGEF7_iLNnbvBLt0KYQzEc10Sle6GuGMtjU6Dka_FtwFun9C7gSboEfr3-arz0VuxNB_qW8JXLqIWVpddI/s1600/alien2.png"
dl assets/ships/alien3.png      "$BV/AVvXsEihETOQd5hrjp2ctPORb7USeE9yTVEa1aILkPO8TLoO1sSlfgZemvhCmrC5viRj363DGjB29I18nZgJTHhdT_hwO0XrI8ng6_lDKA8OaLsLJc-UuKvoA_CeRxfVVQ-rMOA4n6rmPKSdYO0/s1600/alien3.png"
dl assets/ships/alien4.png      "$BV/AVvXsEioszn1JWVZGYSML5ls5izagPd1ww618lK46N3oxqFfNQP-f78JtEthloyPJhAjp1PKpATxVIsUwIxUiVMXVzjpVEbr7yYOIZU8JJgPUm3Wjiw7-43SNYwTUtq85IIa6gtLZZUZpfhAPWc/s1600/alien4.png"
dl assets/ships/greenship1.png  "$BV/AVvXsEjaSUochoweRKrG8OW1lbvmJjO4t3I8IEQuBoj1Nzl_DQnVk7qTx1EQD65mk9UB470Ufipiiyia2Rp3cXArAEfRnsqLylohmiAj4QH_9VdXTNTsjTG4Q6CW8lkuxDOY0C8x68-XDEoWZto/s1600/greenship1.png"
dl assets/ships/greenship2.png  "$BV/AVvXsEjqQS2ceOmGI4tLvx6fzhodSV2TCQOxifNRwvw9lgjJUdkhk5wYHt3XP2VxrHN9FlpA0M-GdwF1LHtkr2HB9JgAJjLeh5CBg81pYhkP4hrbuYoEv0IGHxVWXtnD0tLaiGt5JQgDm519eLY/s1600/greenship2.png"
dl assets/ships/greenship3.png  "$BV/AVvXsEjZqYfMX39mLD08DRBpgwJoKXR0-xdbefKGSRl3UebeeDCBpNxfefpbqVnRNPQKOZBlByt3yXFMM9wOJ2o3gVKOpGDaJTjooh-oOjmhMOI2CNt8_5ifa0-5d7JQpObaNGTRrxPpBLOOCQw/s1600/greenship3.png"
dl assets/ships/greenship4.png  "$BV/AVvXsEjm-EFjdn2LQy2-IqGDK7VOlBGGwUvmF9a3n4CP2CMd9LyvN74fJWCVkW0tbZTRusNtJs2TzjaHPNsR5HrqD6SzhHCyvGACz8WfIxViH1-Uz-AUbWGHLgVaR7nUiatSl5aG2Z2-pX3XNt8/s1600/greenship4.png"
dl assets/ships/f5s1.png        "$BV/AVvXsEhlIaSm44dfRJ4urKesL7GgF6ymgqFszZJNt8NQRCvSGoypJxM4NXNVKdXIg3tanNcpzfJUfkuL0MPK8e9LF51XXoi6c1mygZGVpvEze_R5YivEOS4mveafDjzAoJpdkt4TUPluME7hse8/s1600/F5S1.png"
dl assets/ships/f5s2.png        "$BV/AVvXsEimrnu6XTH1nttTosNyg2uM5gid0bTXngI-jCxDgwrSVqB1XTyJCayCRE_WwiISnESr9ofTaFRsZSOI53EzB7SOpw2vr916SokWkmpVHiFwly4DK7oS2jKssSrUF6ck-9U2PVUdpYYilDY/s1600/F5S2.png"
dl assets/ships/f5s3.png        "$BV/AVvXsEhC7yGTt3Ks2e9a3c38s9ZaiffGoVSXhJ2nHYzdxjzH7GvlpSDU76_E4HDR8jDtWWVBEXuvZHI_v_iIEE91QlF0YO8xUbzAS-QKEndLjZNsounnsGOhXoUmXi6zyu_ukRZ5EflOitMilgY/s1600/F5S3.png"
dl assets/ships/f5s4.png        "$BV/AVvXsEhllOuo0DesFjSZTSVtK04U07xX84K6hOE2AOGimq6T2rC8mIBpjA8ZZEAERbQOKSedGbF8ZuX3-FU_LWD6pwLO4rzkUU4GNinWwc4ETMwlSmzUcSB9Sj5e6S9TrHQec_UQj0y7F_WiIVk/s1600/F5S4.png"
dl assets/ships/redship4.png    "$BV/AVvXsEjnejpBvMZQt_kMUWKc9r8yWIv5-Zo2OIuohgYuq0Y6fh2-xE5wYahLJ2nxRMGRIb2oMFoIGiRD28MJcMrvZmxd_TYK7uxrl9kQaU3CWT6jpx_Enr3dx2Jfm9yrqV2ASYeiRtplpOrD4DE/s1600/redship4.png"
dl assets/ships/rd1.png         "$BV/AVvXsEij5zckI2c9iG91tKFxr_k864yc47QytOOWQC0vbHNdNtpChG3WJg8s6QM3In1eSMJmVSg7T11_7gOZEQW_H9a9dcnvkqDKLlhCKDCt8mpRv7StZWOs1ksd5eL-bXvWjYYZedR93YdYYeg/s1600/RD1.png"
dl assets/ships/rd2.png         "$BV/AVvXsEhx29XQSCnfUm_8VQYa3wmDlbEptNRMHU2StvmRnm7Ku7fI1_p8PRj5gCqLJnLbNwjveM4CWLk22jA3u1dpd6RcyzkKYvov-H7jJzEovp6nBd3Ot0bburYGrs1t4Zol51gtiFFFzlkHPMI/s1600/RD2.png"
dl assets/ships/rd3.png         "$BV/AVvXsEg6m-neRCMXemx1sQLRRYkBtx5-CHaUXPG4i-sYrPE4Ng4qus8vl1qOXx5jdL6bD0cRMk3YBiucotAYsLnnHsGT5KIvA1JP129kU9QUkqMeZ3Q1eGsiEdHXkp9UYwVapdvUmpTC3IVosyw/s1600/RD3.png"
dl assets/ships/att2.png        "$BV/AVvXsEhxT8vsjbDBiyw6s28D0vOBahZiVruaxc-uKwH5d4RM-IGiheGciR1Ynk5ygyA2f0USEChc2gyvSj_vKkn-aMp4e-fIB2O8wlv9hT1btcuHmdhpdC9IWNXt0OfOB4s34XXy59wye2vEyBk/s1600/att2.png"
dl assets/ships/att3.png        "$BV/AVvXsEg47SBKQKDvTMoaO3Wu8acL8lkGtJPcj2s-eRB3dTQ4sh_n38MP58eQGGQ91vN4mD5ZlZsdnbePPmuDr0J9sUvC8Jx5pOQp6ywR_vHLDnicotzKbWeA_EzkGoO-qcoW7s0A05TDvQVcnIU/s1600/att3.png"
dl assets/ships/bgbattleship.png "$BV/AVvXsEhVa-OFXM8HFQ1x8lVJ_hryNQL0iWvkr2XHXEVx10o9gO6Jkvf8UaXf4QK0figEvl2QgUrB0mZmu0vzeexV2NmuLZVAMYO5NDEoYKOdjDOY8F6agL1v7QRR2gybScxJShuPQWioBxRjmGg/s1600/bgbattleship.png"
dl assets/ships/medfighter.png  "$BV/AVvXsEjtM1lnngSORr6wUk0E_MUu3W0idgSm0R56NPTn4QVUWMsgOG0aBIsiaMcaM4NAAkZFyyaIzc8hAMr6Rih-6NouPSmFamsZGZ6tBjaYfHMOacKCu9lhViVIcwEFoJuHwgxC6NCZRRfZbfE/s1600/medfighter.png"
dl assets/ships/speedship.png   "$BV/AVvXsEhhUQkbrxbTXL_pGq9uzerDGSuewLrB_s0f6ozsimQjNdsm8u4kYES-qtszxt0DcJvx_NKgctQRf2-2OUiJ2UkTGP06sH-o9WUuqN5EmdsQGKyC7hZ3cA5kiEGAUjlwDjNHjZ9CHL_y2ew/s1600/speedship.png"
dl assets/ships/spshipspr1.png  "$BV/AVvXsEjcUjA1iUhl7E5M9zi6P7d_d56becLEc5-Yu9ys8eSyi37Ff0H3GJvgzrxDDUkNTMqxsAZgxWDOTLbUW-YQ14k8rSOfmUnAwgGJMJy4hA1alj5P0IPlyTtZI8Wz737sxf85Q7y5eItkd6k/s1600/spshipspr1.png"
dl assets/ships/bgspeedship.png "$BV/AVvXsEh0CGoJxIa7_88WqzidcsYzKhdO-bNbe4lAtueI1gZNUDR9cf9JMm3beTLkrZctjOstkED3bhdugrj1MOm1ZBpT0HsBcIGyXz265xGyTYC7ou197LxfSh5qlvn3zpo1jmBsRLtENnyzexA/s1600/bgspeedship.png"
dl assets/ships/medfrighter.png "$BV/AVvXsEgvaq1DFCxTxx3sGMv3UZIaazU5qwdb7IJ3YsX318aH_BAI00TTmCJTmeM75-J1iIl-HprJQ9v2njrm-zljAP43Y6ZWJgDdyc4JgoLmuEnyDKlaoFwbtbzIGV-u0ysetkt3iXtDvbFl0B4/s1600/medfrighter.png"
dl assets/ships/heavyfreighter.png "$BV/AVvXsEgHM7NSdSRUHsB8BnC7pTOrYHZw19b5J_RsO2xLZOhgy4Da2tlmfylj1zNM3VXlq9vps7lmmxxC2hya2sVWc3XMLQMsoYibSpfZK6nXIEeoxpomxvIR0lWlYRA1b8uaQp-aLQm18xI7fus/s1600/heavyfreighter.png"
dl assets/ships/smallfreighterspr.png "$BV/AVvXsEhnYTAgIwQ0_QvQQfhxxPaFiKDOL-qbsbFFa-oy_w42z04hfupReHFRl7y76yqBi5Hsl4O1gKYCGAWCwjDSrejG46LkVqFXT21tJR0x9TW2bDx4bgMTHjRPLt5LpRz71w4FwBEDRUwMjp4/s1600/smallfreighterspr.png"
dl assets/ships/spaceshipspr.png "$BV/AVvXsEhGu_vKl0POnx7qMxV01o7Zk3jAEfGd-oQQvYltARPyekGAAj7DSmzl1lN5VvBPpKpRQY78r4jptVqMlXY0e5oC_uNm3TE4l1uGJXFAvpFmymF6UQ_aWxApNnG-Dipv2llobHzqgW8OqXI/s1600/spaceshipspr.png"
dl assets/ships/spshipsprite.png "$BV/AVvXsEixQ8KHZbpLzRJjUoVm5DPNUyEgezMOcBs5KiWqUQRmgKopUGdsgHZU_oU1zlkm5PQH_-d_T2Q97u0F0rxMkclWHqrfN3QEdP-iagoZjXPPINjG8Q59D3hn5eWFuXTIbgrzO8QRxxzeU9g/s1600/spshipsprite.png"
dl assets/ships/aliensprite.png  "$BV/AVvXsEik1kl_1Qx9onhJjELHk2i15x0lduIkMZtdE8xbInjVJIxGarq4O9YWRnLBbexrBSdq4vUktyhzId2nKbwiKR0TbL7tQFUvg1GKtlNkg2iRl53MlXoZS7i7mg01HYue00vVzDIhjhxcs1I/s1600/aliensprite.png"
dl assets/ships/aliensprite2.png "$BV/AVvXsEi5IXayE0bMDvxRuQNVAD4Zg2L9FN9_nRRPUH8Z83_8zZyE9e7N8g4dRqcfyVTxssB52vGxcOE4LfGrFyhD0DnUEevBOPyL6XmHO3Oxjg5bHmnBim8wKQ56iunBlRdJxwm935gMlghqt6c/s1600/aliensprite2.png"
dl assets/ships/alienspaceship.png "$BV/AVvXsEiYRDlW467ZdK7UW6pStveXf187KTNudKCOi20NRGm3cIQZul6hPWtOumOHN1ZRBXUlf0SVCHFra7V_lEL0NdVVDLyv4TvKoYDiFDz4ZDbxbw9qpqZi5bBsTul6nYZQz1R3MhTyfXjuYUs/s1600/alienspaceship.png"

# ---- MillionthVector stations (CC-BY 4.0) ----
dl assets/stations/spacestation.png "$BV/AVvXsEhO2lzKDwp6qNafw-kzP9_yzWugZvdv2uK8E9CQ0M3LFUr2U4b0AuSY8MfmNq-q7REitkLtiNavcH4JSnIv8ZcqhUnRt9ZOHk3ER2NL9j7A50lcXp3C8z4KxCLYpZrlmtEhWGr0nS2nr54/s1600/spacestation.png"
dl assets/stations/mainbase.png     "$BV/AVvXsEjltM-iEdBh2rtpe1EGkG83FmzVcxyz5oJNsGO9UkiCrZhHZX_Nb0awwgQv1Ptz6qRp8l4_ThXmPwC3ykS3SZT5IRCiNa36jpc5q5eeBOxteLfEFUlPr0YbG_bNVkF_17uCn8oiZBiTK0A/s1600/mainbase.png"

echo "Done. Kenney Particle Pack must be fetched separately (see ATTRIBUTION.md)."

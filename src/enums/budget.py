from enum import Enum

class RecurringType(Enum):
    ONE_OFF = 1
    WEEKLY = 2
    BI_WEEKLY = 3
    MONTHLY = 4
    YEARLY = 5

class ExpenseType(Enum):
    FIXED = 1
    VARIABLE = 2

class PaymentType(Enum):
    CASH = 1
    CREDIT = 2
    DEBIT = 3
    DIGITAL_WALLET = 4
    BANK_TRANSFER = 5
    CRYPTO = 6
    INVESTMENT = 7
    OTHER = 99
    
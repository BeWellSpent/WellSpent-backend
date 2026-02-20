class BudgetException(Exception):
    """Base exception for budget-related errors."""
    def __init__(self, message: str):
        super().__init__(message)

class BudgetNameAlreadyExistsException(BudgetException):
    """Exception raised when a budget with the same name already exists for the user."""

class BudgetAddPeopleException(BudgetException):
    """Exception raised when there is an error adding people to a budget."""
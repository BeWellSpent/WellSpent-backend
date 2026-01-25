from sqlmodel import select, Session
from src.db.model.budget import BudgetModel
from src.models.budget import Budget
from uuid import uuid4

class BudgetRepository:
    def __init__(self):
        self._model = BudgetModel

    def get_budget_by_user_id(self, user_id: int):
        statement = select(self._model).where(self._model.user_id == user_id)
        result = self.session.exec(statement).all()
        return result
    
    def create_budget(self, session: Session, budget_data: Budget):
        new_budget = BudgetModel()
        new_budget.id = uuid4()
        new_budget.name = budget_data.name
        new_budget.enabled = True
        new_budget.parent = True
        session.add(new_budget)
        session.commit()
        return new_budget
        
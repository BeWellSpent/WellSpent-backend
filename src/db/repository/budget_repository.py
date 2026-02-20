from sqlmodel import select, Session
from db.model.budget import BudgetModel, BudgetToPeopleModel
from uuid import uuid4
import logging as log

from exceptions.budget import BudgetAddPeopleException, BudgetNameAlreadyExistsException

class BudgetRepository:
    def get_all(self, session: Session) -> list[BudgetModel]:
        """Get all budgets"""
        statement = select(BudgetModel)
        result = session.exec(statement).all()
        # Access relationship while session is open to prevent expiry issues
        for budget in result:
            _ = budget.people_mappings
        log.info("[BudgetRepository.get_all] Retrieved budgets: %s", result)
        return result

    def get_budget_by_id(self, session: Session, budget_id: str) -> BudgetModel | None:
        """Get budget by ID"""
        statement = select(BudgetModel).where(BudgetModel.id == budget_id)
        result = session.exec(statement).first()
        # Access relationship while session is open to prevent expiry issues
        # if result:
        #     _ = result.people_mappings
        log.info("[BudgetRepository.get_budget_by_id] Retrieved budget: %s", result)
        return result

    def get_budget_by_user_id(self, session: Session, user_id: int) -> list[BudgetModel]:
        """Get all budgets for a user"""
        statement = select(BudgetModel).where(BudgetModel.user_id == user_id)
        result = session.exec(statement).all()
        return result
    
    def get_people_by_budget_id(self, budget_id: str, session: Session) -> list[BudgetToPeopleModel]:
        """Get all people mappings for a budget"""
        statement = select(BudgetToPeopleModel).where(BudgetToPeopleModel.budget_id == budget_id)
        result = session.exec(statement).all()
        return result
    
    def create_budget(self, session: Session, budget_data: BudgetCreateRequest) -> BudgetModel | None:
        """Create a new budget"""
        budgets = self.get_budget_by_user_id(session, budget_data.user_id)
        for budget in budgets:
            if budget and budget.name == budget_data.name:
                log.error("[BudgetRepository.create_budget] Budget with name %s already exists for user %s", budget_data.name, budget_data.user_id)
                raise BudgetNameAlreadyExistsException(f"Budget with name {budget_data.name} already exists for user {budget_data.user_id}")
        
        log.info("[BudgetRepository.create_budget] Existing budgets for user %s: %s", budget_data.user_id, budgets)
        new_budget = BudgetModel()
        new_budget.id = str(uuid4())
        new_budget.name = budget_data.name
        new_budget.user_id = budget_data.user_id
        new_budget.active = True
        try:
            session.add(new_budget)
            session.commit()
            session.refresh(new_budget)
        except Exception as e:
            log.error("[BudgetRepository.create_budget] Error creating budget: %s", e)
            log.exception(e)
            session.rollback()
            return None
            
        return new_budget
        
    def add_people_to_budget(self, session: Session, budget_id: str, people: list[Person]) -> BudgetModel:
        """Add people to a budget"""
        budget = self.get_budget_by_id(session, budget_id)
        log.info("[BudgetRepository.add_people_to_budget] Retrieved budget for id %s: %s", budget_id, budget)
        if not budget:
            raise ValueError("Budget not found")
        
        # Get existing user names already mapped to this budget
        existing_user_names = {mapping.user_name for mapping in (budget.people_mappings or [])}
        
        # Create new mappings for each person
        for person in people:
            if person.name in existing_user_names:
                raise BudgetAddPeopleException(f"Person {person.name} already in budget, cannot add again.")
            
            new_mapping = BudgetToPeopleModel(
                budget_id=budget_id,
                user_name=person.name
            )
            session.add(new_mapping)
        
        session.commit()
        # Refresh the budget object to get updated people_mappings
        session.refresh(budget)
        return budget 